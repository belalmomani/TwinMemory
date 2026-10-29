package com.belalmomani.twinmemory.data.db

import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.PrimaryKey

@Entity(tableName = "folders")
data class FolderEntity(@PrimaryKey val id: String, val name: String, val parentId: String? = null, val createdAt: Long)

/** The item record only. The physical file lives in FileStore (relPath); device copies and sync state arrive in Phase 4. */
@Entity(tableName = "items", indices = [Index("folderId"), Index("trashedAt"), Index("createdAt"), Index("sha256")])
data class ItemEntity(
    @PrimaryKey val id: String,
    val kind: String,
    val title: String,
    val text: String?,
    val fileName: String?,
    val mimeType: String?,
    val sizeBytes: Long,
    val sha256: String?,
    val relPath: String?,
    val sourceDevice: String,
    val folderId: String?,
    val favorite: Boolean,
    val createdAt: Long,
    val updatedAt: Long,
    val trashedAt: Long?,
)

@Entity(
    tableName = "item_tags", primaryKeys = ["itemId", "tag"],
    foreignKeys = [ForeignKey(entity = ItemEntity::class, parentColumns = ["id"], childColumns = ["itemId"], onDelete = ForeignKey.CASCADE)],
    indices = [Index("tag")],
)
data class ItemTag(val itemId: String, val tag: String)
