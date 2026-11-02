package com.belalmomani.twinmemory.data

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import androidx.room.withTransaction
import com.belalmomani.twinmemory.data.db.*
import com.belalmomani.twinmemory.data.storage.FileStore
import com.belalmomani.twinmemory.domain.Classify
import com.belalmomani.twinmemory.domain.ItemKind
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.withContext
import java.io.File
import java.util.UUID

enum class SaveResult { OK, DUPLICATE, FAILED }
enum class Recv { OK, BAD, ERROR }

class ItemRepository(private val ctx: Context, private val db: AppDatabase, private val store: FileStore) {
    private val items = db.items()
    private val folderDao = db.folders()

    fun observe(useFolder: Boolean, folderId: String?, kind: ItemKind?, favOnly: Boolean, pattern: String?): Flow<List<ItemEntity>> =
        items.observe(if (useFolder) 1 else 0, folderId, kind?.name, if (favOnly) 1 else 0, pattern)
    fun trash() = items.observeTrash()
    fun folders() = folderDao.observe()
    fun tags() = items.observeTags()
    fun totalSize() = items.observeTotalSize()
    fun fileFor(item: ItemEntity): File? = store.file(item.relPath)

    private fun now() = System.currentTimeMillis()
    private fun newId() = UUID.randomUUID().toString()

    suspend fun saveText(text: String, folderId: String?, device: String = "phone"): SaveResult = withContext(Dispatchers.IO) {
        val t = text.trim()
        if (t.isEmpty()) return@withContext SaveResult.FAILED
        val link = Classify.isSingleUrl(t)
        val ts = now()
        runCatching {
            items.insert(
                ItemEntity(newId(), (if (link) ItemKind.LINK else ItemKind.TEXT).name,
                    if (link) Classify.hostOf(t) else Classify.titleFor(t), t, null, "text/plain",
                    t.toByteArray().size.toLong(), null, null, device, folderId, false, ts, ts, null)
            )
        }.fold({ SaveResult.OK }, { SaveResult.FAILED })
    }

    suspend fun importUri(uri: Uri, folderId: String?, device: String = "phone"): SaveResult = importUriId(uri, folderId, device).first

    /** Copies the picked/shared content into app storage immediately; never touches the original. Also returns the new item id. */
    suspend fun importUriId(uri: Uri, folderId: String?, device: String = "phone"): Pair<SaveResult, String?> = withContext(Dispatchers.IO) {
        val id = newId()
        try {
            var name = "file"
            ctx.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use {
                if (it.moveToFirst()) it.getString(0)?.let { n -> name = n }
            }
            val mime = ctx.contentResolver.getType(uri)
            val stored = ctx.contentResolver.openInputStream(uri)?.use { store.store(id, name, it) }
                ?: return@withContext SaveResult.FAILED to null
            val dup = items.countSha(stored.sha256) > 0
            val ts = now()
            try {
                items.insert(
                    ItemEntity(id, Classify.kindFromMime(mime, name).name, name, null, name, mime, stored.size,
                        stored.sha256, stored.relPath, device, folderId, false, ts, ts, null)
                )
            } catch (e: Exception) { store.delete(id); return@withContext SaveResult.FAILED to null }
            (if (dup) SaveResult.DUPLICATE else SaveResult.OK) to id
        } catch (e: Exception) { // includes IOException (e.g. storage full); existing items are never touched
            store.delete(id); SaveResult.FAILED to null
        }
    }

    suspend fun addFolder(name: String): String {
        val id = newId()
        folderDao.insert(FolderEntity(id, name, null, now()))
        return id
    }

    suspend fun createFolder(name: String, parentId: String? = null) {
        val n = name.trim().replace('/', '_').replace('\\', '_')
        if (n.isNotEmpty()) folderDao.insert(FolderEntity(newId(), n, parentId, now()))
    }

    /** "A/B/C" for a folder id (null for the root). */
    suspend fun folderPath(id: String?): String? {
        if (id == null) return null
        val byId = folderDao.list().associateBy { it.id }
        val parts = ArrayDeque<String>(); var cur = byId[id]; var g = 0
        while (cur != null && g++ < 50) { parts.addFirst(cur.name); cur = cur.parentId?.let { byId[it] } }
        return if (parts.isEmpty()) null else parts.joinToString("/")
    }

    /** Creates the chain "A/B/C" if missing (case-insensitive) and returns the last folder id. Ignores "." and "..". */
    suspend fun ensureFolderPath(path: String?): String? {
        if (path.isNullOrBlank()) return null
        val segs = path.split('/', '\\').map { it.trim() }.filter { it.isNotEmpty() && it != "." && it != ".." }.take(20)
        if (segs.isEmpty()) return null
        val all = folderDao.list().toMutableList()
        var parent: String? = null
        for (raw in segs) {
            val seg = raw.take(100)
            val ex = all.firstOrNull { it.parentId == parent && it.name.equals(seg, true) }
            if (ex != null) { parent = ex.id; continue }
            val f = FolderEntity(newId(), seg, parent, now())
            folderDao.insert(f); all += f; parent = f.id
        }
        return parent
    }
    suspend fun renameFolder(id: String, name: String) { val n = name.trim(); if (n.isNotEmpty()) folderDao.rename(id, n) }
    suspend fun rename(id: String, title: String) { val t = title.trim(); if (t.isNotEmpty()) items.rename(id, t, now()) }
    suspend fun setFavorite(id: String, fav: Boolean) = items.setFavorite(id, fav, now())
    suspend fun move(id: String, folderId: String?) = items.move(id, folderId, now())
    suspend fun trash(id: String) = items.setTrashed(id, now(), now())
    suspend fun restore(id: String) = items.setTrashed(id, null, now())

    /** Permanent deletion: only ever called from an explicit, confirmed user action. */
    suspend fun deleteForever(id: String) = withContext(Dispatchers.IO) {
        db.withTransaction { items.delete(id) }
        store.delete(id)
    }

    suspend fun addTag(id: String, tag: String) { val t = tag.trim().take(30); if (t.isNotEmpty()) items.addTag(ItemTag(id, t)) }
    suspend fun removeTag(id: String, tag: String) = items.removeTag(ItemTag(id, tag))

    // ---- items received from the computer (id = the computer's transfer id, so a repeated delivery never duplicates) ----
    suspend fun receiveText(id: String, kind: String, title: String, text: String, folderId: String? = null): Recv = withContext(Dispatchers.IO) {
        try {
            val ts = now()
            items.insert(ItemEntity(id, kind, title.ifBlank { Classify.titleFor(text) }, text, null, "text/plain",
                text.toByteArray().size.toLong(), null, null, "pc", folderId, false, ts, ts, null))
            Recv.OK
        } catch (e: Exception) { Recv.ERROR }
    }

    suspend fun receiveFile(id: String, kind: String, name: String, mime: String?, file: File, size: Long, sha: String, folderId: String? = null): Recv = withContext(Dispatchers.IO) {
        try {
            if (file.length() != size) return@withContext Recv.BAD
            val st = file.inputStream().use { store.store(id, name, it) }
            if (st.sha256 != sha.lowercase()) { store.delete(id); return@withContext Recv.BAD }
            try {
                val ts = now()
                items.insert(ItemEntity(id, kind, name, null, name, mime?.ifBlank { null }, st.size, st.sha256, st.relPath, "pc", folderId, false, ts, ts, null))
            } catch (e: Exception) { store.delete(id); return@withContext Recv.ERROR }
            file.delete()
            Recv.OK
        } catch (e: Exception) { store.delete(id); Recv.ERROR }
    }
}
