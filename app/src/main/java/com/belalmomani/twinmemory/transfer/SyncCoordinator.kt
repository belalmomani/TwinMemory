package com.belalmomani.twinmemory.transfer

import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.net.PcClient
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** One sync cycle at a time (foreground loop and background worker share this lock). Returns how many items arrived from the computer. */
class SyncCoordinator(private val app: TwinMemoryApp) {
    private val lock = Mutex()

    suspend fun cycle(paired: Boolean, client: PcClient?): Int = lock.withLock {
        var received = 0
        if (paired) app.transfers.enqueueNewPhoneItems()
        if (client != null) {
            try { received += InboxSync(app).run(client) } catch (e: Exception) { /* computer unreachable: retried later */ }
            app.transfers.kickIfIdleWithWork()
        }
        try { MirrorSync(app).run() } catch (e: Exception) { /* folder unavailable: retried later */ }
        received
    }
}
