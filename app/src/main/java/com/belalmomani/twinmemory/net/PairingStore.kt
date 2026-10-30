package com.belalmomani.twinmemory.net

import android.content.Context
import java.util.UUID

data class Pairing(val host: String, val port: Int, val pin: String, val token: String, val pcName: String, val deviceId: String)

/** App-private storage (sandboxed, backups disabled). Holds the computer address, its pinned certificate fingerprint and our device token. */
class PairingStore(ctx: Context) {
    private val sp = ctx.getSharedPreferences("pairing", Context.MODE_PRIVATE)

    val deviceId: String
        get() {
            var id = sp.getString("deviceId", null)
            if (id == null) { id = UUID.randomUUID().toString(); sp.edit().putString("deviceId", id).apply() }
            return id
        }

    fun load(): Pairing? {
        val host = sp.getString("host", null) ?: return null
        val pin = sp.getString("pin", null) ?: return null
        val token = sp.getString("token", null) ?: return null
        return Pairing(host, sp.getInt("port", 47821), pin, token, sp.getString("pcName", "") ?: "", deviceId)
    }

    fun save(p: Pairing) {
        sp.edit().putString("host", p.host).putInt("port", p.port).putString("pin", p.pin).putString("token", p.token).putString("pcName", p.pcName).apply()
    }

    fun clear() { sp.edit().remove("host").remove("port").remove("pin").remove("token").remove("pcName").apply() }
}
