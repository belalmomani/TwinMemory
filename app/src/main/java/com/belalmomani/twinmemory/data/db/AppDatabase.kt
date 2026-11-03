package com.belalmomani.twinmemory.data.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

/** No destructive fallback: schema changes ship as migrations so user data is never wiped. */
@Database(entities = [FolderEntity::class, ItemEntity::class, ItemTag::class, TransferEntity::class, MirrorEntry::class], version = 4, exportSchema = true)
abstract class AppDatabase : RoomDatabase() {
    abstract fun items(): ItemDao
    abstract fun folders(): FolderDao
    abstract fun transfers(): TransferDao
    abstract fun mirror(): MirrorDao

    companion object {
        val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    "CREATE TABLE IF NOT EXISTS `transfers` (`id` TEXT NOT NULL, `itemId` TEXT NOT NULL, `title` TEXT NOT NULL, `state` TEXT NOT NULL, " +
                        "`size` INTEGER NOT NULL, `sent` INTEGER NOT NULL, `attempts` INTEGER NOT NULL, `error` TEXT, `createdAt` INTEGER NOT NULL, " +
                        "`updatedAt` INTEGER NOT NULL, PRIMARY KEY(`id`))"
                )
            }
        }

        val MIGRATION_2_3 = object : Migration(2, 3) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS `mirror_map` (`path` TEXT NOT NULL, `itemId` TEXT NOT NULL, `size` INTEGER NOT NULL, PRIMARY KEY(`path`))")
            }
        }

        val MIGRATION_3_4 = object : Migration(3, 4) {
            override fun migrate(db: SupportSQLiteDatabase) {
                // syncId = same id on both devices; for items already transferred it is the transfer id both sides know
                db.execSQL("ALTER TABLE `items` ADD COLUMN `syncId` TEXT NOT NULL DEFAULT ''")
                db.execSQL("UPDATE `items` SET `syncId` = COALESCE((SELECT t.`id` FROM `transfers` t WHERE t.`itemId` = `items`.`id` AND t.`state` = 'DONE' LIMIT 1), `id`)")
            }
        }

        fun create(ctx: Context): AppDatabase =
            Room.databaseBuilder(ctx, AppDatabase::class.java, "twinmemory.db").addMigrations(MIGRATION_1_2, MIGRATION_2_3, MIGRATION_3_4).build()
    }
}
