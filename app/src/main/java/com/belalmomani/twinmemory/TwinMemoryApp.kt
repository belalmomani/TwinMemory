package com.belalmomani.twinmemory

import android.app.Application
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import com.belalmomani.twinmemory.data.ItemRepository
import com.belalmomani.twinmemory.data.db.AppDatabase
import com.belalmomani.twinmemory.data.storage.FileStore
import com.belalmomani.twinmemory.net.MirrorPrefs
import com.belalmomani.twinmemory.net.PairingStore
import com.belalmomani.twinmemory.transfer.PeriodicSyncWorker
import com.belalmomani.twinmemory.transfer.SyncCoordinator
import com.belalmomani.twinmemory.transfer.TransferRepository
import java.util.concurrent.TimeUnit

class TwinMemoryApp : Application() {
    val db: AppDatabase by lazy { AppDatabase.create(this) }
    val fileStore: FileStore by lazy { FileStore(this) }
    val repo: ItemRepository by lazy { ItemRepository(this, db, fileStore) }
    val pairing: PairingStore by lazy { PairingStore(this) }
    val mirrorPrefs: MirrorPrefs by lazy { MirrorPrefs(this) }
    val transfers: TransferRepository by lazy { TransferRepository(this, db) }
    val sync: SyncCoordinator by lazy { SyncCoordinator(this) }

    override fun onCreate() {
        super.onCreate()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            "periodic_sync", ExistingPeriodicWorkPolicy.KEEP,
            PeriodicWorkRequestBuilder<PeriodicSyncWorker>(15, TimeUnit.MINUTES).build(),
        )
    }
}
