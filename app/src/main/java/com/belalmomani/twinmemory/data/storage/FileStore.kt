package com.belalmomani.twinmemory.data.storage

import android.content.Context
import com.belalmomani.twinmemory.domain.Classify
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest

/** App-private storage: filesDir/items/<itemId>/<safe name>. Paths are canonical-checked against the root. */
class FileStore(ctx: Context) {
    private val root = File(ctx.filesDir, "items").apply { mkdirs() }
    private val rootPath = root.canonicalPath + File.separator

    data class Stored(val relPath: String, val size: Long, val sha256: String)

    @Throws(IOException::class)
    fun store(id: String, name: String, input: InputStream): Stored {
        val safe = Classify.sanitizeFileName(name)
        val dir = File(root, id).apply { mkdirs() }
        val out = File(dir, safe)
        if (!out.canonicalPath.startsWith(rootPath)) throw IOException("bad path")
        val md = MessageDigest.getInstance("SHA-256")
        var total = 0L
        try {
            out.outputStream().use { os ->
                val buf = ByteArray(64 * 1024)
                while (true) {
                    val n = input.read(buf)
                    if (n < 0) break
                    os.write(buf, 0, n); md.update(buf, 0, n); total += n
                }
            }
        } catch (e: IOException) {
            dir.deleteRecursively(); throw e
        }
        return Stored("$id/$safe", total, md.digest().joinToString("") { "%02x".format(it) })
    }

    fun file(relPath: String?): File? {
        if (relPath == null) return null
        val f = File(root, relPath)
        return if (f.canonicalPath.startsWith(rootPath) && f.exists()) f else null
    }

    fun delete(id: String) { File(root, id).deleteRecursively() }
}
