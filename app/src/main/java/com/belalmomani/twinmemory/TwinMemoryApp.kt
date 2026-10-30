package com.belalmomani.twinmemory

import android.app.Application
import com.belalmomani.twinmemory.data.ItemRepository
import com.belalmomani.twinmemory.data.db.AppDatabase
import com.belalmomani.twinmemory.data.storage.FileStore
import com.belalmomani.twinmemory.net.PairingStore
import com.belalmomani.twinmemory.transfer.TransferRepository

class TwinMemoryApp : Application() {
    val db: AppDatabase by lazy { AppDatabase.create(this) }
    val fileStore: FileStore by lazy { FileStore(this) }
    val repo: ItemRepository by lazy { ItemRepository(this, db, fileStore) }
    val pairing: PairingStore by lazy { PairingStore(this) }
    val transfers: TransferRepository by lazy { TransferRepository(this, db) }
}
