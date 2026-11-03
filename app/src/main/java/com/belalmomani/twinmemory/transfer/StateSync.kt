package com.belalmomani.twinmemory.transfer

import android.content.Context
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.data.db.ItemEntity
import com.belalmomani.twinmemory.net.PcClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/**
 * Propagates rename, trash/restore ("delete" only moves to Trash), folder moves and text edits between the phone and the computer.
 * Rule: the other device's change wins if this item did not change here since the last exchange, or if it is newer. Text edits that
 * collide are never overwritten: the other version is kept as a "conflict copy". Nothing is permanently deleted by sync.
 * The very first exchange only records a baseline (nothing is changed), so old differences never overwrite anything.
 */
class StateSync(private val app: TwinMemoryApp) {
    private val sp = app.getSharedPreferences("state_sync", Context.MODE_PRIVATE)

    private suspend fun folderPaths(): Map<String, String> {
        val rows = app.db.folders().list()
        val byId = rows.associateBy { it.id }
        return rows.associate { f ->
            val parts = ArrayDeque<String>(); var cur: com.belalmomani.twinmemory.data.db.FolderEntity? = f; var g = 0
            while (cur != null && g++ < 50) { parts.addFirst(cur.name); cur = cur.parentId?.let { byId[it] } }
            f.id to parts.joinToString("/")
        }
    }

    private fun toJson(i: ItemEntity, paths: Map<String, String>): JSONObject = JSONObject()
        .put("syncId", i.syncId).put("kind", i.kind).put("title", i.title).put("trashed", i.trashedAt != null).put("updated", i.updatedAt)
        .put("folderPath", i.folderId?.let { paths[it] } ?: "")
        .also { if ((i.kind == "TEXT" || i.kind == "LINK") && (i.text?.length ?: 0) <= 100_000) it.put("text", i.text) }

    suspend fun run(client: PcClient) = withContext(Dispatchers.IO) {
        val dao = app.db.items()
        val pcCursor = sp.getLong("pc", 0L)
        val localCursor = sp.getLong("local", 0L)
        val baseline = localCursor == 0L
        val startedAt = System.currentTimeMillis()
        val paths = folderPaths()
        val req = JSONObject().put("since", pcCursor).put("baseline", baseline)
        if (!baseline) {
            val arr = JSONArray()
            for (i in dao.changedSince(localCursor - 120_000)) arr.put(toJson(i, paths))
            req.put("changes", arr)
        }
        val resp = client.sync(req)
        if (!baseline) {
            val ch = resp.optJSONArray("changes")
            if (ch != null) for (k in 0 until ch.length()) apply(ch.getJSONObject(k), localCursor, paths)
        }
        sp.edit().putLong("local", startedAt).putLong("pc", resp.getLong("serverTime")).apply()
    }

    private suspend fun apply(ch: JSONObject, cursor: Long, paths: Map<String, String>) {
        val syncId = ch.optString("syncId")
        if (syncId.isEmpty()) return
        val dao = app.db.items()
        val item = dao.bySync(syncId) ?: return
        val rUpd = ch.optLong("updated")
        val localChanged = item.updatedAt > cursor
        val remoteWins = !localChanged || rUpd > item.updatedAt
        val now = System.currentTimeMillis()

        if ((item.kind == "TEXT" || item.kind == "LINK") && ch.has("text") && !ch.isNull("text")) {
            val rt = ch.getString("text").trim()
            if (rt.isNotEmpty() && rt != (item.text ?: "").trim()) {
                if (!localChanged) dao.setText(item.id, rt, rt.toByteArray().size.toLong(), now)
                else {
                    val id = UUID.randomUUID().toString()
                    app.repo.insertRaw(ItemEntity(id, item.kind, item.title + " (" + app.getString(R.string.conflict_copy) + ")", rt, null, "text/plain",
                        rt.toByteArray().size.toLong(), null, null, "phone", item.folderId, false, now, now, null)) // both versions are kept
                }
            }
        }
        val title = ch.optString("title")
        if (remoteWins && title.isNotBlank() && title != item.title) dao.rename(item.id, title, now)
        val rTrashed = ch.optBoolean("trashed")
        if (remoteWins && rTrashed != (item.trashedAt != null)) dao.setTrashed(item.id, if (rTrashed) now else null, now)
        val fp = ch.optString("folderPath")
        val curPath = item.folderId?.let { paths[it] } ?: ""
        if (remoteWins && !fp.equals(curPath, true)) dao.move(item.id, app.repo.ensureFolderPath(fp), now)
    }
}
