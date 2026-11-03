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
 * Two-way mirror between the app and a folder the user chose on the phone (visible in any file manager), including nested folders.
 * App items are written there as files inside matching directories; new files/directories the user puts there are imported
 * (and then auto-sent to the computer). Nothing is ever overwritten (names get " (2)") or deleted. Edits/renames/deletions are not propagated.
 */
class MirrorSync(private val app: TwinMemoryApp) {
    private val ctx = app.applicationContext

    private fun dirName(n: String) = Classify.sanitizeFileName(n).trimEnd('.', ' ').ifEmpty { "folder" }

    suspend fun run(): Int = withContext(Dispatchers.IO) {
        val uriStr = app.mirrorPrefs.treeUri ?: return@withContext 0
        val cats = app.mirrorPrefs.cats() ?: return@withContext 0
        val root = DocumentFile.fromTreeUri(ctx, Uri.parse(uriStr))
        if (root == null || !root.exists() || !root.canWrite()) return@withContext 0
        val repo = app.repo
        val dao = app.db.mirror()
        val mapped = dao.all().map { it.path }.toHashSet()
        var changed = 0
        val now = System.currentTimeMillis()

        // 1) walk the folder (any depth)
        val found = mutableListOf<Pair<String, DocumentFile>>()
        val dirs = mutableListOf<String>()
        fun walk(dir: DocumentFile, prefix: String, depth: Int) {
            if (depth > 20) return
            for (f in dir.listFiles()) {
                val n = f.name ?: continue
                if (n.startsWith(".")) continue
                if (f.isDirectory) { dirs += prefix + n; walk(f, "$prefix$n/", depth + 1) }
                else if (f.isFile) found += (prefix + n) to f
            }
        }
        walk(root, "", 0)

        // "A/B/file" -> folder path "A/B"; files directly under a category directory (Texts, Files...) belong to no folder
        fun folderPathOf(rel: String): String? {
            val d = rel.split('/').dropLast(1)
            if (d.isEmpty()) return null
            val fp = if (cats.any { it.equals(d[0], true) }) d.drop(1) else d
            return fp.takeIf { it.isNotEmpty() }?.joinToString("/")
        }

        // directories (also empty ones) -> folders
        for (d in dirs) folderPathOf("$d/x")?.let { repo.ensureFolderPath(it) }

        // new files -> items
        for ((rel, f) in found) {
            val name = f.name ?: continue
            if (rel in mapped || name.endsWith(".tmp") || name.endsWith(".part") || name.endsWith(".crdownload")) continue
            if (now - f.lastModified() < 3000) continue // still being written
            val fid = repo.ensureFolderPath(folderPathOf(rel))
            val (_, id) = repo.importUriId(f.uri, fid, "phone")
            if (id != null) { dao.put(MirrorEntry(rel, id, f.length())); changed++ }
        }

        // 2) app folders and items -> directories and files
        fun dirFor(segs: List<String>): DocumentFile? {
            var cur: DocumentFile = root
            for (s in segs) cur = cur.findFile(s)?.takeIf { it.isDirectory } ?: cur.createDirectory(s) ?: return null
            return cur
        }
        val folderRows = app.db.folders().list()
        val byId = folderRows.associateBy { it.id }
        fun pathOfFolder(id: String?): List<String>? {
            if (id == null) return null
            val parts = ArrayDeque<String>(); var cur = byId[id]; var g = 0
            while (g++ < 50) { val f = cur ?: break; parts.addFirst(dirName(f.name)); cur = f.parentId?.let { byId[it] } }
            return if (parts.isEmpty()) null else parts.toList()
        }
        for (f in folderRows) {
            val segs = pathOfFolder(f.id) ?: continue
            val key = "dir:" + segs.joinToString("/")
            if (key in mapped) continue
            if (dirFor(segs) != null) dao.put(MirrorEntry(key, "folder:" + f.id, 0))
        }
        for (it in app.db.items().unmirrored()) {
            val segs = pathOfFolder(it.folderId) ?: listOf(cats[when (it.kind) { "TEXT" -> 0; "LINK" -> 1; "IMAGE" -> 2; else -> 3 }])
            val dir = dirFor(segs) ?: continue
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
            dao.put(MirrorEntry(segs.joinToString("/") + "/" + doc.name, it.id, doc.length()))
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
