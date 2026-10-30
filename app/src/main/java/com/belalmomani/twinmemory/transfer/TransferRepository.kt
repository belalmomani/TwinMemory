package com.belalmomani.twinmemory.transfer

import android.content.Context
import androidx.work.BackoffPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import com.belalmomani.twinmemory.data.db.AppDatabase
import com.belalmomani.twinmemory.data.db.ItemEntity
import com.belalmomani.twinmemory.data.db.TransferEntity
import java.util.UUID
import java.util.concurrent.TimeUnit

class TransferRepository(private val ctx: Context, private val db: AppDatabase) {
    private val dao = db.transfers()
    fun observe() = dao.observe()

    suspend fun enqueue(items: List<ItemEntity>): Int {
        var n = 0
        for (i in items) {
            if (dao.activeFor(i.id) > 0) continue // never queue the same item twice
            val now = System.currentTimeMillis()
            dao.insert(TransferEntity(UUID.randomUUID().toString(), i.id, i.title, "PENDING", i.sizeBytes, 0, 0, null, now, now))
            n++
        }
        if (n > 0) kick()
        return n
    }

    suspend fun retry(id: String) { dao.retry(id, System.currentTimeMillis()); kick() }

    /** Restart the worker when the computer is reachable and nothing is mid-transfer. */
    suspend fun kickIfIdleWithWork() {
        val a = dao.active()
        if (a.isNotEmpty() && a.none { it.state == "SENDING" }) kick()
    }

    fun kick() {
        WorkManager.getInstance(ctx).enqueueUniqueWork(
            "send_queue", ExistingWorkPolicy.REPLACE,
            OneTimeWorkRequestBuilder<SendWorker>().setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.SECONDS).build(),
        )
    }
}
