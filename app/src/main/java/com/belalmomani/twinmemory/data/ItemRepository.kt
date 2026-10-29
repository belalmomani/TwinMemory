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

    /** Copies the picked/shared content into app storage immediately; never touches the original. */
    suspend fun importUri(uri: Uri, folderId: String?, device: String = "phone"): SaveResult = withContext(Dispatchers.IO) {
        val id = newId()
        try {
            var name = "file"
            ctx.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use {
                if (it.moveToFirst()) it.getString(0)?.let { n -> name = n }
            }
            val mime = ctx.contentResolver.getType(uri)
            val stored = ctx.contentResolver.openInputStream(uri)?.use { store.store(id, name, it) }
                ?: return@withContext SaveResult.FAILED
            val dup = items.countSha(stored.sha256) > 0
            val ts = now()
            try {
                items.insert(
                    ItemEntity(id, Classify.kindFromMime(mime, name).name, name, null, name, mime, stored.size,
                        stored.sha256, stored.relPath, device, folderId, false, ts, ts, null)
                )
            } catch (e: Exception) { store.delete(id); return@withContext SaveResult.FAILED }
            if (dup) SaveResult.DUPLICATE else SaveResult.OK
        } catch (e: Exception) { // includes IOException (e.g. storage full); existing items are never touched
            store.delete(id); SaveResult.FAILED
        }
    }

    suspend fun createFolder(name: String) { val n = name.trim(); if (n.isNotEmpty()) folderDao.insert(FolderEntity(newId(), n, null, now())) }
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
}
