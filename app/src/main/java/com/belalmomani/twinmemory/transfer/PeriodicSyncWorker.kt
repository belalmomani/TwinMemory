package com.belalmomani.twinmemory.transfer

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.net.PcClient

/** Runs about every 15 minutes (Android's minimum) even when the app is closed; quick no-op when the computer is not reachable. */
class PeriodicSyncWorker(ctx: Context, params: WorkerParameters) : CoroutineWorker(ctx, params) {
    override suspend fun doWork(): Result {
        val app = applicationContext as TwinMemoryApp
        try {
            val p = app.pairing.load()
            var client: PcClient? = null
            if (p != null) {
                val c = PcClient(p.host, p.port, p.pin, p.token, p.deviceId)
                client = try { if (c.ping()) c else null } catch (e: Exception) { null }
            }
            app.sync.cycle(p != null, client)
        } catch (e: Exception) { /* next run */ }
        return Result.success()
    }
}
