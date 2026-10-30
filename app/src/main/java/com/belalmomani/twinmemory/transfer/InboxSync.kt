package com.belalmomani.twinmemory.transfer

import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.data.Recv
import com.belalmomani.twinmemory.net.PcClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Pulls items the computer queued for this phone (while the app is open). Each file is downloaded with resume, size and SHA-256 are
 * verified before it becomes an item, and only then is the computer told "ack". The item id equals the transfer id, so a repeated
 * delivery after a lost ack is recognised and just acknowledged again.
 */
class InboxSync(private val app: TwinMemoryApp) {
    private val allowed = setOf("TEXT", "LINK", "IMAGE", "VIDEO", "DOCUMENT", "FILE")
    private val idRegex = Regex("^[0-9a-fA-F-]{36}$")

    /** Returns how many new items arrived. IOExceptions (computer unreachable) propagate to the caller. */
    suspend fun run(client: PcClient): Int = withContext(Dispatchers.IO) {
        val list = client.listOutbox()
        val dir = File(app.cacheDir, "inbox")
        var received = 0
        for (k in 0 until list.length()) {
            val o = list.getJSONObject(k)
            val id = o.optString("id")
            val kind = o.optString("kind")
            if (!idRegex.matches(id) || kind !in allowed) continue
            if (app.db.items().get(id) != null) { client.ack(id); continue }
            try {
                val result = if (kind == "TEXT" || kind == "LINK") {
                    app.repo.receiveText(id, kind, o.optString("title"), o.optString("text"))
                } else {
                    val part = File(dir, "$id.part")
                    val size = o.optLong("size")
                    if (part.exists() && part.length() > size) part.delete()
                    client.download(id, part)
                    val r = app.repo.receiveFile(id, kind, o.optString("fileName").ifBlank { o.optString("title") }, o.optString("mime"), part, size, o.optString("sha256"))
                    if (r == Recv.BAD) part.delete()
                    r
                }
                when (result) {
                    Recv.OK -> { client.ack(id); received++ }
                    Recv.BAD -> client.fail(id)
                    Recv.ERROR -> {} // e.g. storage full: nothing is lost, try again later
                }
            } catch (e: PcClient.HttpError) {
                if (e.code == 404 || e.code == 410) { try { client.fail(id) } catch (ignored: Exception) {} } else throw e
            }
        }
        received
    }
}
