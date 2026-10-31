package com.belalmomani.twinmemory.ui

import android.app.Application
import android.net.Uri
import android.os.Build
import androidx.documentfile.provider.DocumentFile
import android.content.Intent
import androidx.annotation.StringRes
import androidx.core.content.IntentCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.data.SaveResult
import com.belalmomani.twinmemory.data.db.ItemEntity
import com.belalmomani.twinmemory.domain.Classify
import com.belalmomani.twinmemory.domain.ItemKind
import com.belalmomani.twinmemory.domain.SortBy
import com.belalmomani.twinmemory.net.Discovery
import com.belalmomani.twinmemory.transfer.InboxSync
import com.belalmomani.twinmemory.net.Pairing
import com.belalmomani.twinmemory.net.PcClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch

fun sortItems(l: List<ItemEntity>, s: SortBy): List<ItemEntity> = when (s) {
    SortBy.DATE -> l.sortedByDescending { it.createdAt }
    SortBy.NAME -> l.sortedBy { it.title.lowercase() }
    SortBy.SIZE -> l.sortedByDescending { it.sizeBytes }
    SortBy.TYPE -> l.sortedWith(compareBy({ it.kind }, { it.title.lowercase() }))
}

enum class Conn { UNPAIRED, OFFLINE, ONLINE }

@OptIn(ExperimentalCoroutinesApi::class)
class MainViewModel(app: Application) : AndroidViewModel(app) {
    private val repo = (app as TwinMemoryApp).repo
    val query = MutableStateFlow("")
    val kind = MutableStateFlow<ItemKind?>(null)
    val sort = MutableStateFlow(SortBy.DATE)
    val openFolder = MutableStateFlow<String?>(null)
    val msgs = MutableSharedFlow<Int>(extraBufferCapacity = 8)

    private fun <T> Flow<T>.keep(init: T) = stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), init)

    val home = combine(query, kind) { q, k -> q to k }
        .flatMapLatest { (q, k) -> repo.observe(false, null, k, false, Classify.likePattern(q)) }
        .combine(sort, ::sortItems).keep(emptyList())
    val favorites = repo.observe(false, null, null, true, null).combine(sort, ::sortItems).keep(emptyList())
    val folderItems = openFolder.flatMapLatest { f ->
        if (f == null) flowOf(emptyList()) else repo.observe(true, f, null, false, null)
    }.combine(sort, ::sortItems).keep(emptyList())
    val folders = repo.folders().keep(emptyList())
    val trash = repo.trash().keep(emptyList())
    val tags = repo.tags().map { l -> l.groupBy({ it.itemId }, { it.tag }) }.keep(emptyMap())
    val totalSize = repo.totalSize().map { it ?: 0L }.keep(0L)

    // ---- computer connection (Phase 4) ----
    private val tapp = getApplication<TwinMemoryApp>()
    val conn = MutableStateFlow(Conn.UNPAIRED)
    val pcName = MutableStateFlow(tapp.pairing.load()?.pcName)
    val transfersList = tapp.transfers.observe().keep(emptyList())
    val found = MutableStateFlow<List<Discovery.Found>>(emptyList())
    val busy = MutableStateFlow(false)
    val diag = MutableStateFlow("")
    val mirrorName = MutableStateFlow(tapp.mirrorPrefs.treeUri?.let { DocumentFile.fromTreeUri(tapp, Uri.parse(it))?.name })

    fun setMirrorFolder(uri: Uri) = viewModelScope.launch(Dispatchers.IO) {
        try { tapp.contentResolver.takePersistableUriPermission(uri, android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION) } catch (e: Exception) { }
        val cats = listOf(R.string.cat_texts, R.string.cat_links, R.string.cat_photos, R.string.cat_files).map { tapp.getString(it) }
        tapp.mirrorPrefs.save(uri.toString(), cats)
        mirrorName.value = DocumentFile.fromTreeUri(tapp, uri)?.name
        msgs.tryEmit(R.string.msg_folder_set)
        try { tapp.sync.cycle(tapp.pairing.load() != null, null) } catch (e: Exception) { }
    }

    fun clearMirrorFolder() { tapp.mirrorPrefs.clear(); mirrorName.value = null }

    /** Accepts "192.168.1.5", "192.168.1.5:47821" or "https://192.168.1.5:47821/". */
    private fun parseHost(raw: String, defPort: Int): Pair<String, Int> {
        var h = raw.trim().removePrefix("https://").removePrefix("http://").substringBefore('/')
        var p = defPort
        if (h.count { it == ':' } == 1) { p = h.substringAfter(':').toIntOrNull() ?: defPort; h = h.substringBefore(':') }
        return h to p
    }

    private fun describe(e: Exception): String = when (e) {
        is PcClient.HttpError -> "HTTP ${e.code} ${e.body.take(120)}"
        else -> e.javaClass.simpleName + ": " + (e.message ?: "")
    }

    fun testConnection(host: String, port: Int) = viewModelScope.launch(Dispatchers.IO) {
        val (h, p) = parseHost(host, port)
        busy.value = true; diag.value = ""
        try {
            val j = PcClient(h, p, null, null, tapp.pairing.deviceId).info()
            diag.value = "OK: " + j.optString("name") + " (" + h + ":" + p + ")"
        } catch (e: Exception) { diag.value = describe(e) }
        finally { busy.value = false }
    }

    init {
        if (tapp.pairing.load() != null) tapp.transfers.kick()
        viewModelScope.launch(Dispatchers.IO) { while (true) { pingOnce(); delay(8000) } }
    }

    private fun client(p: Pairing) = PcClient(p.host, p.port, p.pin, p.token, p.deviceId)

    private suspend fun pingOnce() {
        val p = tapp.pairing.load()
        if (p == null) {
            conn.value = Conn.UNPAIRED; pcName.value = null
            try { tapp.sync.cycle(false, null) } catch (e: Exception) { }
            return
        }
        pcName.value = p.pcName
        conn.value = try {
            if (client(p).ping()) Conn.ONLINE else Conn.OFFLINE
        } catch (e: PcClient.HttpError) {
            if (e.code == 401 || e.code == 403) { tapp.pairing.clear(); msgs.tryEmit(R.string.msg_unpaired_remote); Conn.UNPAIRED } else Conn.OFFLINE
        } catch (e: Exception) { Conn.OFFLINE }
        val n = try { tapp.sync.cycle(true, if (conn.value == Conn.ONLINE) client(p) else null) } catch (e: Exception) { 0 }
        if (n > 0) msgs.tryEmit(R.string.msg_received)
    }

    fun pair(host: String, port: Int, code: String) = viewModelScope.launch(Dispatchers.IO) {
        busy.value = true
        try {
            val (h, pt) = parseHost(host, port)
            diag.value = ""
            val r = PcClient(h, pt, null, null, tapp.pairing.deviceId).pair(code, Build.MODEL ?: "Android")
            tapp.pairing.save(Pairing(h, pt, r.fingerprint, r.token, r.pcName, tapp.pairing.deviceId))
            msgs.tryEmit(R.string.msg_paired)
            pingOnce(); tapp.transfers.kick()
        } catch (e: PcClient.HttpError) {
            diag.value = describe(e)
            msgs.tryEmit(if (e.code == 403) R.string.msg_pair_bad_code else R.string.msg_pair_unreachable)
        } catch (e: Exception) { diag.value = describe(e); msgs.tryEmit(R.string.msg_pair_unreachable) }
        finally { busy.value = false }
    }

    fun unpair() = viewModelScope.launch(Dispatchers.IO) {
        tapp.pairing.load()?.let { runCatching { client(it).unpair() } } // best effort; the computer may be off
        tapp.pairing.clear(); conn.value = Conn.UNPAIRED; pcName.value = null
    }

    fun discover() = viewModelScope.launch { found.value = Discovery.find(); if (found.value.isEmpty()) msgs.tryEmit(R.string.none_found) }

    fun sendToPc(list: List<ItemEntity>) = viewModelScope.launch {
        if (tapp.pairing.load() == null) { msgs.tryEmit(R.string.msg_need_pair); return@launch }
        tapp.transfers.enqueue(list); msgs.tryEmit(R.string.msg_queued)
    }

    fun retry(id: String) = viewModelScope.launch { tapp.transfers.retry(id) }

    private fun report(r: SaveResult) {
        msgs.tryEmit(when (r) { SaveResult.OK -> R.string.msg_saved; SaveResult.DUPLICATE -> R.string.msg_duplicate; SaveResult.FAILED -> R.string.msg_save_failed })
    }
    private suspend fun autoSend() { if (tapp.pairing.load() != null) tapp.transfers.enqueueNewPhoneItems() }
    fun saveText(t: String) = viewModelScope.launch { report(repo.saveText(t, openFolder.value)); autoSend() }
    fun importUri(u: Uri) = viewModelScope.launch { report(repo.importUri(u, openFolder.value)); autoSend() }
    fun handleShare(i: Intent) {
        when (i.action) {
            Intent.ACTION_SEND -> {
                val u = IntentCompat.getParcelableExtra(i, Intent.EXTRA_STREAM, Uri::class.java)
                val t = i.getStringExtra(Intent.EXTRA_TEXT)
                if (u != null) importUri(u) else if (!t.isNullOrBlank()) saveText(t)
            }
            Intent.ACTION_SEND_MULTIPLE -> IntentCompat.getParcelableArrayListExtra(i, Intent.EXTRA_STREAM, Uri::class.java)?.forEach { importUri(it) }
        }
    }
    fun createFolder(n: String) = viewModelScope.launch { repo.createFolder(n) }
    fun renameFolder(id: String, n: String) = viewModelScope.launch { repo.renameFolder(id, n) }
    fun rename(i: ItemEntity, t: String) = viewModelScope.launch { repo.rename(i.id, t) }
    fun toggleFavorite(i: ItemEntity) = viewModelScope.launch { repo.setFavorite(i.id, !i.favorite) }
    fun move(i: ItemEntity, folder: String?) = viewModelScope.launch { repo.move(i.id, folder) }
    fun trash(i: ItemEntity) = viewModelScope.launch { repo.trash(i.id); msgs.tryEmit(R.string.msg_trashed) }
    fun restore(i: ItemEntity) = viewModelScope.launch { repo.restore(i.id); msgs.tryEmit(R.string.msg_restored) }
    fun deleteForever(i: ItemEntity) = viewModelScope.launch { repo.deleteForever(i.id); msgs.tryEmit(R.string.msg_deleted_forever) }
    fun addTag(i: ItemEntity, t: String) = viewModelScope.launch { repo.addTag(i.id, t) }
    fun removeTag(i: ItemEntity, t: String) = viewModelScope.launch { repo.removeTag(i.id, t) }
    fun fileFor(i: ItemEntity) = repo.fileFor(i)
    fun toast(@StringRes id: Int) { msgs.tryEmit(id) }
}
