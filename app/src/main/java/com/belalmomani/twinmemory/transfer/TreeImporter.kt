package com.belalmomani.twinmemory.transfer

import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.data.SaveResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.security.MessageDigest

/**
 * Imports a directory tree picked with the system folder picker.
 * Copy: originals are untouched. Move: an original is deleted only after the stored copy was verified (same size and SHA-256 as the
 * source right now); only directories that are completely empty afterwards are removed.
 */
class TreeImporter(private val app: TwinMemoryApp) {
    data class Result(val imported: Int, val removed: Int)

    private fun sourceSha(f: DocumentFile): String? = try {
        app.contentResolver.openInputStream(f.uri)?.use { i ->
            val md = MessageDigest.getInstance("SHA-256"); val b = ByteArray(64 * 1024)
            while (true) { val n = i.read(b); if (n < 0) break; md.update(b, 0, n) }
            md.digest().joinToString("") { "%02x".format(it) }
        }
    } catch (e: Exception) { null }

    private suspend fun verifiedRemove(f: DocumentFile, itemId: String): Boolean {
        val item = app.db.items().get(itemId) ?: return false
        val stored = app.fileStore.file(item.relPath) ?: return false
        if (stored.length() != item.sizeBytes || f.length() != item.sizeBytes) return false
        if (sourceSha(f) != item.sha256) return false
        return try { f.delete() } catch (e: Exception) { false }
    }

    suspend fun run(tree: Uri, baseFolderId: String?, move: Boolean = false): Result = withContext(Dispatchers.IO) {
        val root = DocumentFile.fromTreeUri(app, tree) ?: return@withContext Result(0, 0)
        val start = listOfNotNull(app.repo.folderPath(baseFolderId), root.name).joinToString("/")
        app.repo.ensureFolderPath(start)
        var n = 0; var removed = 0
        suspend fun walk(dir: DocumentFile, path: String, depth: Int) {
            if (depth > 20) return
            for (f in dir.listFiles()) {
                val name = f.name ?: continue
                if (name.startsWith(".")) continue
                if (f.isDirectory) {
                    val p = "$path/$name"
                    app.repo.ensureFolderPath(p)
                    walk(f, p, depth + 1)
                } else if (f.isFile) {
                    val fid = app.repo.ensureFolderPath(path)
                    val (r, id) = app.repo.importUriId(f.uri, fid, "phone")
                    if (r != SaveResult.FAILED && id != null) {
                        n++
                        if (move && verifiedRemove(f, id)) removed++
                    }
                }
            }
        }
        walk(root, start, 0)
        if (move) {
            fun prune(dir: DocumentFile): Boolean {
                for (c in dir.listFiles()) if (c.isDirectory && prune(c)) c.delete() // delete() on a directory is recursive: only ever called when empty
                return dir.listFiles().isEmpty()
            }
            if (prune(root)) root.delete()
        }
        Result(n, removed)
    }
}
