package com.belalmomani.twinmemory.ui

import androidx.annotation.StringRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.background
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.ui.theme.TM

@Composable
private fun IconBox(icon: ImageVector, tint: Color, bg: Color, size: Int = 40) {
    Box(Modifier.size(size.dp).background(bg, RoundedCornerShape(12.dp)), Alignment.Center) { Icon(icon, null, tint = tint) }
}

@Composable
private fun Tile(icon: ImageVector, tint: Color, bg: Color, @StringRes label: Int, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Card(
        colors = CardDefaults.cardColors(containerColor = TM.Surface), border = BorderStroke(1.dp, TM.Outline),
        modifier = modifier.clickable(onClick = onClick),
    ) {
        Column(Modifier.fillMaxWidth().padding(vertical = 12.dp, horizontal = 4.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
            IconBox(icon, tint, bg)
            Text(stringResource(label), fontSize = 12.sp, maxLines = 1)
        }
    }
}

@Composable
private fun ZoneRow(icon: ImageVector, @StringRes name: Int, subtitle: String, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(14.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        IconBox(icon, TM.Primary, TM.PrimarySoft)
        Column(Modifier.weight(1f)) {
            Text(stringResource(name), fontWeight = FontWeight.SemiBold)
            Text(subtitle, color = TM.Muted, fontSize = 12.sp)
        }
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = TM.Muted)
    }
}

/** Home dashboard matching the approved design. Every tile opens a real screen; counts come from the database. */
@Composable
fun Dashboard(countOf: (String) -> String, onRoute: (String) -> Unit, onTrash: () -> Unit, onSettings: () -> Unit, onHelp: () -> Unit) {
    Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(16.dp, 4.dp, 16.dp, 96.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val m = Modifier.weight(1f)
            Tile(Icons.Filled.Folder, Color(0xFFE07B12), Color(0xFFFDEBD6), R.string.cat_files, { onRoute("cat:files") }, m)
            Tile(Icons.Filled.Image, Color(0xFF6A4FD6), Color(0xFFEAE6FB), R.string.cat_photos, { onRoute("cat:photos") }, m)
            Tile(Icons.Filled.Link, Color(0xFF1F8A5B), Color(0xFFDFF3E8), R.string.cat_links, { onRoute("cat:links") }, m)
            Tile(Icons.Filled.Description, TM.Primary, TM.PrimarySoft, R.string.cat_texts, { onRoute("cat:texts") }, m)
        }
        Text(stringResource(R.string.section_folders), fontWeight = FontWeight.Bold)
        Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), border = BorderStroke(1.dp, TM.Outline), modifier = Modifier.fillMaxWidth()) {
            ZoneRow(Icons.Filled.PhoneAndroid, R.string.zone_phone, countOf("zone:c")) { onRoute("zone:c") }
            HorizontalDivider(color = TM.Outline)
            ZoneRow(Icons.Filled.Computer, R.string.zone_pc, countOf("zone:d")) { onRoute("zone:d") }
            HorizontalDivider(color = TM.Outline)
            ZoneRow(Icons.Filled.Cloud, R.string.zone_shared, countOf("zone:all")) { onRoute("zone:all") }
        }
        Text(stringResource(R.string.section_tools), fontWeight = FontWeight.Bold)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val m = Modifier.weight(1f)
            Tile(Icons.Filled.Star, Color(0xFFE0A100), Color(0xFFFFF4D6), R.string.nav_favorites, { onRoute("favs") }, m)
            Tile(Icons.Filled.Delete, TM.Muted, TM.IdleBg, R.string.trash, onTrash, m)
            Tile(Icons.Filled.Download, TM.Primary, TM.PrimarySoft, R.string.tool_queue, { onRoute("queue") }, m)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val m = Modifier.weight(1f)
            Tile(Icons.Filled.Help, TM.Primary, TM.PrimarySoft, R.string.tool_help, onHelp, m)
            Tile(Icons.Filled.Settings, TM.Muted, TM.IdleBg, R.string.tool_settings, onSettings, m)
        }
    }
}
