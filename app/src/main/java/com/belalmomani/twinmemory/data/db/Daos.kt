package com.belalmomani.twinmemory.data.db

import androidx.room.*
import kotlinx.coroutines.flow.Flow

@Dao
interface ItemDao {
    @Query(
        """SELECT DISTINCT i.* FROM items i LEFT JOIN item_tags t ON t.itemId = i.id
        WHERE i.trashedAt IS NULL
        AND (:useFolder = 0 OR i.folderId IS :folderId)
        AND (:kind IS NULL OR i.kind = :kind)
        AND (:favOnly = 0 OR i.favorite = 1)
        AND (:pattern IS NULL OR i.title LIKE :pattern ESCAPE '\' OR i.text LIKE :pattern ESCAPE '\'
             OR i.fileName LIKE :pattern ESCAPE '\' OR i.sourceDevice LIKE :pattern ESCAPE '\' OR t.tag LIKE :pattern ESCAPE '\')
        ORDER BY i.createdAt DESC"""
    )
    fun observe(useFolder: Int, folderId: String?, kind: String?, favOnly: Int, pattern: String?): Flow<List<ItemEntity>>

    @Query("SELECT * FROM items WHERE trashedAt IS NOT NULL ORDER BY trashedAt DESC") fun observeTrash(): Flow<List<ItemEntity>>
    @Query("SELECT * FROM items WHERE id = :id") suspend fun get(id: String): ItemEntity?
    @Insert suspend fun insert(item: ItemEntity)
    @Query("UPDATE items SET title = :title, updatedAt = :now WHERE id = :id") suspend fun rename(id: String, title: String, now: Long)
    @Query("UPDATE items SET favorite = :fav, updatedAt = :now WHERE id = :id") suspend fun setFavorite(id: String, fav: Boolean, now: Long)
    @Query("UPDATE items SET folderId = :folderId, updatedAt = :now WHERE id = :id") suspend fun move(id: String, folderId: String?, now: Long)
    @Query("UPDATE items SET trashedAt = :ts, updatedAt = :now WHERE id = :id") suspend fun setTrashed(id: String, ts: Long?, now: Long)
    @Query("DELETE FROM items WHERE id = :id") suspend fun delete(id: String)
    @Query("SELECT COUNT(*) FROM items WHERE sha256 = :sha AND trashedAt IS NULL") suspend fun countSha(sha: String): Int
    @Query("SELECT SUM(sizeBytes) FROM items WHERE trashedAt IS NULL") fun observeTotalSize(): Flow<Long?>
    @Insert(onConflict = OnConflictStrategy.IGNORE) suspend fun addTag(tag: ItemTag)
    @Delete suspend fun removeTag(tag: ItemTag)
    @Query("SELECT * FROM item_tags") fun observeTags(): Flow<List<ItemTag>>
}

@Dao
interface FolderDao {
    @Query("SELECT * FROM folders ORDER BY name COLLATE NOCASE") fun observe(): Flow<List<FolderEntity>>
    @Insert suspend fun insert(folder: FolderEntity)
    @Query("UPDATE folders SET name = :name WHERE id = :id") suspend fun rename(id: String, name: String)
}
