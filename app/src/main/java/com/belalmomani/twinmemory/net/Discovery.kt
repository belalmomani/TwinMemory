package com.belalmomani.twinmemory.net

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException

/** Finds TwinMemory computers on the local network with a UDP broadcast. May be blocked by the router/firewall; manual IP entry always works. */
object Discovery {
    data class Found(val host: String, val port: Int, val name: String)

    suspend fun find(): List<Found> = withContext(Dispatchers.IO) {
        val out = mutableListOf<Found>()
        try {
            DatagramSocket().use { s ->
                s.broadcast = true; s.soTimeout = 1200
                val msg = "TWINMEMORY_DISCOVER".toByteArray()
                s.send(DatagramPacket(msg, msg.size, InetAddress.getByName("255.255.255.255"), 47822))
                val buf = ByteArray(1024)
                val end = System.currentTimeMillis() + 2500
                while (System.currentTimeMillis() < end) {
                    try {
                        val p = DatagramPacket(buf, buf.size)
                        s.receive(p)
                        val j = JSONObject(String(p.data, 0, p.length))
                        if (j.optString("app") == "TwinMemory") out += Found(p.address.hostAddress ?: continue, j.getInt("port"), j.optString("name"))
                    } catch (e: SocketTimeoutException) { break }
                }
            }
        } catch (e: Exception) { /* no network / blocked */ }
        out.distinctBy { it.host }
    }
}
