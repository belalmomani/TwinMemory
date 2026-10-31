package com.belalmomani.twinmemory.transfer

import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.data.db.ItemEntity
import com.belalmomani.twinmemory.data.db.MirrorEntry
import com.belalmomani.twinmemory.domain.Classify
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.IOException

/**
 * Two-way mirror between the app and a folder the user chose on the phone (visible in any file manager).
 * App items are written there as files; new files the user puts there are imported (and then auto-sent to the computer).
 * Safety: nothing is ever overwritten (names get " (2)") and nothing is deleted. Edits/renames/deletions are not propagated yet.
 */
class MirrorSync(private val app: TwinMemoryApp) {
    private val ctx = app.applicationContext

    suspend fun run(): Int = withContext(Dispatchers.IO) {
        val uriStr = app.mirrorPrefs.treeUri ?: return@withContext 0
        val cats = app.mirrorPrefs.cats() ?: return@withContext 0
        val root = DocumentFile.fromTreeUri(ctx, Uri.parse(uriStr))
        if (root == null || !root.exists() || !root.canWrite()) return@withContext 0
        val dao = app.db.mirror()
        val mapped = dao.all().map { it.path }.toHashSet()
        val folders = app.db.folders().list().toMutableList()
        var changed = 0
        val now = System.currentTimeMillis()

        // 1) new files put into the folder -> items
        val found = mutableListOf<Pair<String, DocumentFile>>()
        for (f in root.listFiles()) {
            val n = f.name ?: continue
            if (f.isDirectory) { for (g in f.listFiles()) if (g.isFile) found += "$n/${g.name}" to g }
            else if (f.isFile) found += n to f
        }
        for ((rel, f) in found) {
            val name = f.name ?: continue
            if (rel in mapped || name.startsWith(".") || name.endsWith(".tmp") || name.endsWith(".part") || name.endsWith(".crdownload")) continue
            if (now - f.lastModified() < 3000) continue // still being written
            val dirName = if (rel.contains('/')) rel.substringBefore('/') else null
            val folderId = dirName?.takeIf { d -> cats.none { it.equals(d, true) } }?.let { d ->
                folders.firstOrNull { it.name.equals(d, true) }?.id ?: app.repo.addFolder(d).also { id ->
                    folders += com.belalmomani.twinmemory.data.db.FolderEntity(id, d, null, now)
                }
            }
            val (_, id) = app.repo.importUriId(f.uri, folderId, "phone")
            if (id != null) { dao.put(MirrorEntry(rel, id, f.length())); changed++ }
        }

        // 2) items not yet in the folder -> files
        for (it in app.db.items().unmirrored()) {
            val dirName = it.folderId?.let { fid -> folders.firstOrNull { f -> f.id == fid }?.name }
                ?: cats[when (it.kind) { "TEXT" -> 0; "LINK" -> 1; "IMAGE" -> 2; else -> 3 }]
            val dir = root.findFile(dirName)?.takeIf { d -> d.isDirectory } ?: root.createDirectory(dirName) ?: continue
            val doc = dir.createFile(mimeFor(it), uniqueName(dir, exportName(it))) ?: continue
            try {
                val out = ctx.contentResolver.openOutputStream(doc.uri)
                if (out == null) { doc.delete(); continue }
                out.use { o ->
                    when (it.kind) {
                        "TEXT" -> o.write((it.text ?: "").toByteArray())
                        "LINK" -> o.write("[InternetShortcut]\r\nURL=${(it.text ?: "").trim()}\r\n".toByteArray())
                        else -> {
                            val src = app.fileStore.file(it.relPath) ?: throw IOException("file missing")
                            src.inputStream().use { i -> i.copyTo(o) }
                        }
                    }
                }
            } catch (e: IOException) { doc.delete(); break } // storage full etc.: nothing is lost, retried next cycle
            dao.put(MirrorEntry("${dir.name}/${doc.name}", it.id, doc.length()))
            changed++
        }
        changed
    }

    private fun mimeFor(it: ItemEntity) = when (it.kind) { "TEXT" -> "text/plain"; "LINK" -> "application/octet-stream"; else -> it.mimeType?.ifBlank { null } ?: "application/octet-stream" }

    private fun exportName(it: ItemEntity): String {
        val t = Classify.sanitizeFileName(it.title).trimEnd('.', ' ').ifEmpty { "file" }
        return when (it.kind) {
            "TEXT" -> "$t.txt"
            "LINK" -> "$t.url"
            else -> if (t.substringAfterLast('.', "").isEmpty() && (it.fileName ?: "").contains('.')) t + "." + it.fileName!!.substringAfterLast('.') else t
        }
    }

    private fun uniqueName(dir: DocumentFile, name: String): String {
        if (dir.findFile(name) == null) return name
        val stem = name.substringBeforeLast('.', name); val ext = name.substringAfterLast('.', "")
        var i = 2
        while (true) {
            val n = if (ext.isEmpty()) "$stem ($i)" else "$stem ($i).$ext"
            if (dir.findFile(n) == null) return n
            i++
        }
    }
}
