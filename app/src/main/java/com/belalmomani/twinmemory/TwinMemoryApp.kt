package com.belalmomani.twinmemory

import android.app.Application
import com.belalmomani.twinmemory.data.ItemRepository
import com.belalmomani.twinmemory.data.db.AppDatabase
import com.belalmomani.twinmemory.data.storage.FileStore

class TwinMemoryApp : Application() {
    val repo: ItemRepository by lazy { ItemRepository(this, AppDatabase.create(this), FileStore(this)) }
}
