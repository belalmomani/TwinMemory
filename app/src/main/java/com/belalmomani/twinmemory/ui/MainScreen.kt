package com.belalmomani.twinmemory.ui

import android.text.format.Formatter
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.belalmomani.twinmemory.BuildConfig
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.data.db.ItemEntity
import com.belalmomani.twinmemory.domain.ItemKind
import com.belalmomani.twinmemory.domain.SortBy
import com.belalmomani.twinmemory.ui.components.StatusChip
import com.belalmomani.twinmemory.ui.components.TmStatus
import com.belalmomani.twinmemory.ui.theme.TM

private sealed interface Dlg {
    data object Choice : Dlg
    data object SaveText : Dlg
    data object NewFolder : Dlg
    data class RenameFolder(val id: String, val name: String) : Dlg
    data class Rename(val item: ItemEntity) : Dlg
    data class Move(val item: ItemEntity) : Dlg
    data class Props(val item: ItemEntity) : Dlg
    data class Forever(val item: ItemEntity) : Dlg
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(vm: MainViewModel) {
    val ctx = LocalContext.current
    var tab by rememberSaveable { mutableIntStateOf(0) }
    var showTrash by rememberSaveable { mutableStateOf(false) }
    var route by rememberSaveable { mutableStateOf<String?>(null) }
    var help by remember { mutableStateOf(false) }
    var dlg by remember { mutableStateOf<Dlg?>(null) }
    val snack = remember { SnackbarHostState() }
    LaunchedEffect(Unit) { vm.msgs.collect { snack.showSnackbar(ctx.getString(it)) } }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { l -> l.forEach { vm.importUri(it) } }

    val home by vm.home.collectAsStateWithLifecycle()
    val favs by vm.favorites.collectAsStateWithLifecycle()
    val inFolder by vm.folderItems.collectAsStateWithLifecycle()
    val folders by vm.folders.collectAsStateWithLifecycle()
    val trash by vm.trash.collectAsStateWithLifecycle()
    val tags by vm.tags.collectAsStateWithLifecycle()
    val total by vm.totalSize.collectAsStateWithLifecycle()
    val query by vm.query.collectAsStateWithLifecycle()
    val kind by vm.kind.collectAsStateWithLifecycle()
    val sort by vm.sort.collectAsStateWithLifecycle()
    val openFolder by vm.openFolder.collectAsStateWithLifecycle()
    val conn by vm.conn.collectAsStateWithLifecycle()
    val mirror by vm.mirrorName.collectAsStateWithLifecycle()
    val treePicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { u -> if (u != null) vm.setMirrorFolder(u) }
    val queue by vm.transfersList.collectAsStateWithLifecycle()

    val acts = ItemActions(
        onOpen = { dlg = Dlg.Props(it) },
        onCopy = { copyText(ctx, it); vm.toast(R.string.msg_copied) },
        onMove = { dlg = Dlg.Move(it) },
        onShare = { runCatching { shareItem(ctx, it, vm.fileFor(it)) } },
        onRename = { dlg = Dlg.Rename(it) },
        onFavorite = { vm.toggleFavorite(it) },
        onProps = { dlg = Dlg.Props(it) },
        onTrash = { vm.trash(it) },
        onSend = { vm.sendToPc(listOf(it)) },
    )
    fun forRoute(r: String?): List<ItemEntity> = when (r) {
        "cat:files" -> home.filter { it.kind == "FILE" || it.kind == "DOCUMENT" || it.kind == "VIDEO" }
        "cat:photos" -> home.filter { it.kind == "IMAGE" }
        "cat:links" -> home.filter { it.kind == "LINK" }
        "cat:texts" -> home.filter { it.kind == "TEXT" }
        "zone:c" -> home.filter { it.sourceDevice == "phone" }
        "zone:d" -> home.filter { it.sourceDevice == "pc" }
        "zone:all" -> home
        "favs" -> favs
        else -> emptyList()
    }
    val routeTitle = when (route) {
        "cat:files" -> R.string.cat_files; "cat:photos" -> R.string.cat_photos; "cat:links" -> R.string.cat_links; "cat:texts" -> R.string.cat_texts
        "zone:c" -> R.string.zone_phone; "zone:d" -> R.string.zone_pc; "zone:all" -> R.string.zone_shared
        "favs" -> R.string.nav_favorites; else -> R.string.tool_queue
    }
    val countOf = { r: String ->
        val n = forRoute(r).size
        if (n == 0) ctx.getString(R.string.count_none) else ctx.getString(R.string.count_items, n)
    }
    BackHandler(enabled = route != null || showTrash || openFolder != null) {
        when { showTrash -> showTrash = false; route != null -> route = null; else -> vm.openFolder.value = null }
    }
    val canSave = !showTrash && route != "queue" && route != "favs" && route != "pair" && ((tab == 0) || (tab == 1 && openFolder != null))

    Scaffold(
        containerColor = TM.Background,
        snackbarHost = { SnackbarHost(snack) },
        bottomBar = {
            NavigationBar(containerColor = TM.Surface) {
                listOf(Triple(R.string.nav_home, Icons.Filled.Home, 0), Triple(R.string.nav_folders, Icons.Filled.Folder, 1), Triple(R.string.nav_more, Icons.Filled.MoreHoriz, 2)).forEach { (l, ic, i) ->
                    NavigationBarItem(tab == i, { tab = i; showTrash = false; route = null }, { Icon(ic, null) }, label = { Text(stringResource(l)) })
                }
            }
        },
        floatingActionButton = {
            if (canSave) ExtendedFloatingActionButton({ dlg = Dlg.Choice }, containerColor = TM.Primary, contentColor = androidx.compose.ui.graphics.Color.White) {
                Icon(Icons.Filled.Add, null); Spacer(Modifier.width(8.dp)); Text(stringResource(R.string.save))
            }
        },
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {
            when {
                showTrash -> TrashScreen(trash, { showTrash = false }, { vm.restore(it) }, { dlg = Dlg.Forever(it) })
                route == "pair" -> PairScreen(vm) { route = null }
                route == "queue" -> QueueScreen(queue, { route = null }, { vm.retry(it) })
                route != null -> {
                    Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        IconButton({ route = null }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.back)) }
                        Text(stringResource(routeTitle), fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                        SortButton(sort) { vm.sort.value = it }
                    }
                    val empty = when (route) { "zone:d" -> R.string.zone_pc_empty; "favs" -> R.string.favorites_empty; "queue" -> R.string.queue_empty; else -> R.string.folder_items_empty }
                    ItemList(forRoute(route), empty, acts, Modifier.weight(1f))
                }
                tab == 0 -> {
                    Row(Modifier.padding(16.dp, 12.dp, 16.dp, 0.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.Cloud, null, tint = TM.Primary, modifier = Modifier.size(30.dp))
                        Spacer(Modifier.width(8.dp))
                        Text(stringResource(R.string.app_name), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                        val st = when {
                            conn == Conn.UNPAIRED -> TmStatus.Unpaired
                            conn == Conn.OFFLINE -> TmStatus.Offline
                            queue.any { it.state == "SENDING" } -> TmStatus.Transferring
                            else -> TmStatus.Connected
                        }
                        StatusChip(st, Modifier.clickable { route = "pair" })
                    }
                    OutlinedTextField(query, { vm.query.value = it }, singleLine = true, placeholder = { Text(stringResource(R.string.search_hint)) },
                        leadingIcon = { Icon(Icons.Filled.Search, null) }, modifier = Modifier.fillMaxWidth().padding(16.dp, 8.dp))
                    if (query.isBlank()) Dashboard(countOf, { route = it }, { showTrash = true }, { tab = 2 }, { help = true })
                    else ItemList(home, R.string.no_results, acts, Modifier.weight(1f))
                }
                tab == 1 && openFolder == null -> FoldersScreen(folders, { vm.openFolder.value = it }, { dlg = Dlg.NewFolder }, { id, n -> dlg = Dlg.RenameFolder(id, n) })
                tab == 1 -> {
                    val name = folders.firstOrNull { it.id == openFolder }?.name ?: ""
                    Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        IconButton({ vm.openFolder.value = null }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.back)) }
                        Text(name, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
                    }
                    ItemList(inFolder, R.string.folder_items_empty, acts, Modifier.weight(1f))
                }
                else -> MoreScreen(Formatter.formatShortFileSize(ctx, total), onPair = { route = "pair" }, mirror = mirror, onPick = { treePicker.launch(null) }, onClear = { vm.clearMirrorFolder() }) { showTrash = true }
            }
        }
    }

    if (help) AlertDialog(onDismissRequest = { help = false }, title = { Text(stringResource(R.string.help_title)) },
        text = { Text(stringResource(R.string.help_body)) }, confirmButton = { TextButton({ help = false }) { Text(stringResource(R.string.close)) } })

    when (val d = dlg) {
        null -> {}
        Dlg.Choice -> SaveChoiceDialog({ dlg = Dlg.SaveText }, { picker.launch(arrayOf("*/*")) }, { if (dlg == Dlg.Choice) dlg = null })
        Dlg.SaveText -> TextInputDialog(R.string.save_text_link, R.string.text_hint, multiline = true, onDismiss = { dlg = null }, onOk = { vm.saveText(it) })
        Dlg.NewFolder -> TextInputDialog(R.string.new_folder, R.string.folder_name, confirm = R.string.create, onDismiss = { dlg = null }, onOk = { vm.createFolder(it) })
        is Dlg.RenameFolder -> TextInputDialog(R.string.rename, R.string.folder_name, d.name, onDismiss = { dlg = null }, onOk = { vm.renameFolder(d.id, it) })
        is Dlg.Rename -> TextInputDialog(R.string.rename, R.string.name, d.item.title, onDismiss = { dlg = null }, onOk = { vm.rename(d.item, it) })
        is Dlg.Move -> MoveDialog(folders, { vm.move(d.item, it) }, { dlg = null })
        is Dlg.Props -> PropertiesDialog(d.item, tags[d.item.id].orEmpty(),
            onOpen = { if (!openItem(ctx, d.item, vm.fileFor(d.item))) vm.toast(R.string.msg_cannot_open) },
            onAddTag = { vm.addTag(d.item, it) }, onRemoveTag = { vm.removeTag(d.item, it) }, onDismiss = { dlg = null })
        is Dlg.Forever -> ConfirmDialog(R.string.delete_forever, R.string.delete_forever_msg, R.string.delete_forever, { dlg = null }, { vm.deleteForever(d.item) })
    }
}

@Composable
private fun SortButton(sort: SortBy, onSort: (SortBy) -> Unit) {
    var menu by remember { mutableStateOf(false) }
    Box {
        TextButton({ menu = true }) { Text(stringResource(R.string.sort)) }
        DropdownMenu(menu, { menu = false }) {
            SortBy.entries.forEach { s ->
                DropdownMenuItem({ Text((if (s == sort) "✓ " else "") + stringResource(sortLabel(s))) }, { onSort(s); menu = false })
            }
        }
    }
}

private fun sortLabel(s: SortBy) = when (s) { SortBy.DATE -> R.string.sort_date; SortBy.NAME -> R.string.sort_name; SortBy.SIZE -> R.string.sort_size; SortBy.TYPE -> R.string.sort_type }

@Composable
private fun FoldersScreen(folders: List<com.belalmomani.twinmemory.data.db.FolderEntity>, onOpen: (String) -> Unit, onNew: () -> Unit, onRename: (String, String) -> Unit) {
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.nav_folders), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            Button(onNew) { Text(stringResource(R.string.new_folder)) }
        }
        if (folders.isEmpty()) Box(Modifier.fillMaxSize().padding(32.dp), Alignment.Center) { Text(stringResource(R.string.folders_empty), color = TM.Muted) }
        else LazyColumn(contentPadding = PaddingValues(16.dp, 0.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            items(folders, key = { it.id }) { f ->
                Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), modifier = Modifier.fillMaxWidth()) {
                    Row(Modifier.padding(start = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                        TextButton({ onOpen(f.id) }, Modifier.weight(1f)) { Text(f.name, Modifier.fillMaxWidth()) }
                        IconButton({ onRename(f.id, f.name) }) { Icon(Icons.Filled.Edit, stringResource(R.string.rename)) }
                    }
                }
            }
        }
    }
}

@Composable
private fun TrashScreen(trash: List<ItemEntity>, onBack: () -> Unit, onRestore: (ItemEntity) -> Unit, onForever: (ItemEntity) -> Unit) {
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.back)) }
            Text(stringResource(R.string.trash), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        }
        if (trash.isEmpty()) Box(Modifier.fillMaxSize().padding(32.dp), Alignment.Center) { Text(stringResource(R.string.trash_empty), color = TM.Muted) }
        else LazyColumn(contentPadding = PaddingValues(16.dp, 0.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            items(trash, key = { it.id }) { it2 ->
                Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(14.dp)) {
                        Text(it2.title, fontWeight = FontWeight.SemiBold, maxLines = 1)
                        Row {
                            TextButton({ onRestore(it2) }) { Text(stringResource(R.string.restore)) }
                            TextButton({ onForever(it2) }) { Text(stringResource(R.string.delete_forever), color = TM.ErrorFg) }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun MoreScreen(used: String, onPair: () -> Unit, mirror: String?, onPick: () -> Unit, onClear: () -> Unit, onTrash: () -> Unit) {
    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(stringResource(R.string.nav_more), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(stringResource(R.string.storage_used, used), fontWeight = FontWeight.SemiBold)
                Text(stringResource(R.string.storage_note), color = TM.Muted)
            }
        }
        Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(stringResource(R.string.folder_title), fontWeight = FontWeight.SemiBold)
                Text(if (mirror == null) stringResource(R.string.folder_none) else mirror, color = TM.Muted)
                Text(stringResource(R.string.folder_note), color = TM.Muted, style = MaterialTheme.typography.bodySmall)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onPick) { Text(stringResource(R.string.folder_pick)) }
                    if (mirror != null) OutlinedButton(onClear) { Text(stringResource(R.string.folder_clear)) }
                }
            }
        }
        Button(onPair, Modifier.fillMaxWidth()) { Text(stringResource(R.string.pair_title)) }
        OutlinedButton(onTrash, Modifier.fillMaxWidth()) { Icon(Icons.Filled.Delete, null); Spacer(Modifier.width(8.dp)); Text(stringResource(R.string.trash)) }
        Text(stringResource(R.string.version, BuildConfig.VERSION_NAME, BuildConfig.VERSION_CODE), color = TM.Muted)
    }
}
