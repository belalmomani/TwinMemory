package com.belalmomani.twinmemory.transfer

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.data.db.ItemEntity
import com.belalmomani.twinmemory.data.db.TransferEntity
import com.belalmomani.twinmemory.net.PcClient
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.IOException
import java.io.RandomAccessFile

/**
 * Sends queued items to the paired computer. Resumable (the server reports how many bytes it already has), and an item is marked
 * DONE only after the computer verified size and SHA-256. Offline = WAITING + retry; the original item is never deleted.
 */
class SendWorker(ctx: Context, params: WorkerParameters) : CoroutineWorker(ctx, params) {
    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        val app = applicationContext as TwinMemoryApp
        val pairing = app.pairing.load() ?: return@withContext Result.success()
        val dao = app.db.transfers()
        val items = app.db.items()
        dao.resetSending()
        val client = PcClient(pairing.host, pairing.port, pairing.pin, pairing.token, pairing.deviceId)
        var retryLater = false
        for (t in dao.active()) {
            val item = items.get(t.itemId)
            val now = System.currentTimeMillis()
            if (item == null) { dao.setState(t.id, "FAILED", "item missing", now); continue }
            try {
                dao.bump(t.id)
                dao.setState(t.id, "SENDING", null, now)
                send(client, t, item, app)
                dao.setState(t.id, "DONE", null, System.currentTimeMillis())
            } catch (e: CancellationException) {
                throw e
            } catch (e: PcClient.HttpError) {
                val permanent = e.code == 401 || e.code == 403 || e.code == 413 || e.code == 422 || e.code == 507 || e.code == 400
                if (permanent) dao.setState(t.id, "FAILED", "HTTP ${e.code}", System.currentTimeMillis())
                else { dao.setState(t.id, "WAITING", "HTTP ${e.code}", System.currentTimeMillis()); retryLater = true; break }
            } catch (e: IOException) {
                dao.setState(t.id, "WAITING", e.message, System.currentTimeMillis()); retryLater = true; break
            } catch (e: Exception) {
                dao.setState(t.id, "FAILED", e.message, System.currentTimeMillis())
            }
        }
        if (retryLater) Result.retry() else Result.success()
    }

    private suspend fun send(client: PcClient, t: TransferEntity, item: ItemEntity, app: TwinMemoryApp) {
        val dao = app.db.transfers()
        val isText = item.kind == "TEXT" || item.kind == "LINK"
        val meta = JSONObject().put("id", t.id).put("kind", item.kind).put("title", item.title)
            .put("mime", item.mimeType ?: "").put("size", item.sizeBytes)
        if (isText) meta.put("text", item.text ?: "")
        else meta.put("fileName", item.fileName ?: item.title).put("sha256", item.sha256 ?: "")
        val r = client.startTransfer(meta)
        if (r.optString("state") == "done") return
        if (!isText) {
            val file = app.fileStore.file(item.relPath) ?: throw IllegalStateException("file missing")
            var offset = r.optLong("offset")
            RandomAccessFile(file, "r").use { raf ->
                val buf = ByteArray(2 * 1024 * 1024)
                while (offset < item.sizeBytes) {
                    raf.seek(offset)
                    val n = raf.read(buf, 0, minOf(buf.size.toLong(), item.sizeBytes - offset).toInt())
                    if (n <= 0) break
                    offset = client.putChunk(t.id, offset, buf, n).getLong("received")
                    dao.progress(t.id, offset, System.currentTimeMillis())
                }
            }
        }
        client.complete(t.id)
    }
}
