package com.belalmomani.twinmemory.ui

import android.app.Application
import android.content.Intent
import android.net.Uri
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
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch

fun sortItems(l: List<ItemEntity>, s: SortBy): List<ItemEntity> = when (s) {
    SortBy.DATE -> l.sortedByDescending { it.createdAt }
    SortBy.NAME -> l.sortedBy { it.title.lowercase() }
    SortBy.SIZE -> l.sortedByDescending { it.sizeBytes }
    SortBy.TYPE -> l.sortedWith(compareBy({ it.kind }, { it.title.lowercase() }))
}

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

    private fun report(r: SaveResult) {
        msgs.tryEmit(when (r) { SaveResult.OK -> R.string.msg_saved; SaveResult.DUPLICATE -> R.string.msg_duplicate; SaveResult.FAILED -> R.string.msg_save_failed })
    }
    fun saveText(t: String) = viewModelScope.launch { report(repo.saveText(t, openFolder.value)) }
    fun importUri(u: Uri) = viewModelScope.launch { report(repo.importUri(u, openFolder.value)) }
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
