package com.belalmomani.twinmemory.net

import android.content.Context

/** The phone folder the user picked (Storage Access Framework tree URI) and the four category folder names, fixed at selection time. */
class MirrorPrefs(ctx: Context) {
    private val sp = ctx.getSharedPreferences("mirror", Context.MODE_PRIVATE)
    val treeUri: String? get() = sp.getString("tree", null)
    fun cats(): List<String>? = sp.getString("cats", null)?.split("|")?.takeIf { it.size == 4 }
    fun save(uri: String, cats: List<String>) { sp.edit().putString("tree", uri).putString("cats", cats.joinToString("|")).apply() }
    fun clear() { sp.edit().remove("tree").remove("cats").apply() }
}
