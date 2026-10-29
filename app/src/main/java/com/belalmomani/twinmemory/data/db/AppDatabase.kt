package com.belalmomani.twinmemory.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

/** No destructive fallback: schema changes must ship as migrations so user data is never wiped. */
@Database(entities = [FolderEntity::class, ItemEntity::class, ItemTag::class], version = 1, exportSchema = true)
abstract class AppDatabase : RoomDatabase() {
    abstract fun items(): ItemDao
    abstract fun folders(): FolderDao

    companion object {
        fun create(ctx: Context): AppDatabase = Room.databaseBuilder(ctx, AppDatabase::class.java, "twinmemory.db").build()
    }
}
