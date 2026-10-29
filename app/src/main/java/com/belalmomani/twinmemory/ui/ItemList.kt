package com.belalmomani.twinmemory.ui

import android.text.format.Formatter
import androidx.annotation.StringRes
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.data.db.ItemEntity
import com.belalmomani.twinmemory.domain.ItemKind
import com.belalmomani.twinmemory.ui.theme.TM

@StringRes fun kindLabel(k: String): Int = when (k) {
    "TEXT" -> R.string.kind_text; "LINK" -> R.string.kind_link; "IMAGE" -> R.string.kind_image
    "VIDEO" -> R.string.kind_video; "DOCUMENT" -> R.string.kind_document; else -> R.string.kind_file
}
@StringRes fun deviceLabel(d: String): Int = if (d == "pc") R.string.device_pc else R.string.device_phone

class ItemActions(
    val onOpen: (ItemEntity) -> Unit, val onCopy: (ItemEntity) -> Unit, val onMove: (ItemEntity) -> Unit,
    val onShare: (ItemEntity) -> Unit, val onRename: (ItemEntity) -> Unit, val onFavorite: (ItemEntity) -> Unit,
    val onProps: (ItemEntity) -> Unit, val onTrash: (ItemEntity) -> Unit,
)

@Composable
fun ItemList(list: List<ItemEntity>, @StringRes emptyText: Int, actions: ItemActions, modifier: Modifier = Modifier) {
    if (list.isEmpty()) {
        Box(modifier.fillMaxSize().padding(32.dp), Alignment.Center) { Text(stringResource(emptyText), color = TM.Muted) }
        return
    }
    LazyColumn(modifier, verticalArrangement = Arrangement.spacedBy(8.dp), contentPadding = PaddingValues(16.dp, 8.dp, 16.dp, 96.dp)) {
        items(list, key = { it.id }) { ItemRow(it, actions) }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ItemRow(item: ItemEntity, a: ItemActions) {
    var menu by remember { mutableStateOf(false) }
    val ctx = LocalContext.current
    Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), modifier = Modifier.fillMaxWidth()) {
        Box {
            Row(
                Modifier.fillMaxWidth().combinedClickable(onClick = { a.onOpen(item) }, onLongClick = { menu = true }).padding(14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(Modifier.weight(1f)) {
                    Text(item.title, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    val sub = stringResource(kindLabel(item.kind)) + " · " + stringResource(deviceLabel(item.sourceDevice)) +
                        if (item.sizeBytes > 0) " · " + Formatter.formatShortFileSize(ctx, item.sizeBytes) else ""
                    Text(sub, color = TM.Muted, style = MaterialTheme.typography.bodySmall)
                }
                if (item.favorite) Icon(Icons.Filled.Star, stringResource(R.string.favorite), tint = TM.Primary)
            }
            DropdownMenu(menu, { menu = false }) {
                val isText = item.kind == ItemKind.TEXT.name || item.kind == ItemKind.LINK.name
                fun entry(@StringRes t: Int, f: (ItemEntity) -> Unit) = @Composable { DropdownMenuItem({ Text(stringResource(t)) }, { menu = false; f(item) }) }
                if (isText) entry(R.string.copy, a.onCopy)()
                entry(R.string.move, a.onMove)()
                entry(R.string.share, a.onShare)()
                entry(R.string.rename, a.onRename)()
                entry(if (item.favorite) R.string.unfavorite else R.string.favorite, a.onFavorite)()
                entry(R.string.properties, a.onProps)()
                entry(R.string.delete, a.onTrash)()
            }
        }
    }
}
