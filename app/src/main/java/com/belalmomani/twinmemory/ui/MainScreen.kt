package com.belalmomani.twinmemory.ui

import android.text.format.Formatter
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
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

    val acts = ItemActions(
        onOpen = { dlg = Dlg.Props(it) },
        onCopy = { copyText(ctx, it); vm.toast(R.string.msg_copied) },
        onMove = { dlg = Dlg.Move(it) },
        onShare = { runCatching { shareItem(ctx, it, vm.fileFor(it)) } },
        onRename = { dlg = Dlg.Rename(it) },
        onFavorite = { vm.toggleFavorite(it) },
        onProps = { dlg = Dlg.Props(it) },
        onTrash = { vm.trash(it) },
    )
    val canSave = (tab == 0) || (tab == 1 && openFolder != null)

    Scaffold(
        containerColor = TM.Background,
        snackbarHost = { SnackbarHost(snack) },
        bottomBar = {
            NavigationBar(containerColor = TM.Surface) {
                listOf(
                    Triple(R.string.nav_home, Icons.Filled.Home, 0), Triple(R.string.nav_folders, Icons.Filled.List, 1),
                    Triple(R.string.nav_favorites, Icons.Filled.Star, 2), Triple(R.string.nav_more, Icons.Filled.Menu, 3),
                ).forEach { (l, ic, i) ->
                    NavigationBarItem(tab == i, { tab = i; showTrash = false }, { Icon(ic, null) }, label = { Text(stringResource(l)) })
                }
            }
        },
        floatingActionButton = {
            if (canSave && !showTrash) ExtendedFloatingActionButton({ dlg = Dlg.Choice }, containerColor = TM.Primary, contentColor = androidx.compose.ui.graphics.Color.White) {
                Icon(Icons.Filled.Add, null); Spacer(Modifier.width(8.dp)); Text(stringResource(R.string.save))
            }
        },
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {
            when {
                showTrash -> TrashScreen(trash, { showTrash = false }, { vm.restore(it) }, { dlg = Dlg.Forever(it) })
                tab == 0 -> {
                    Row(Modifier.padding(16.dp, 12.dp, 16.dp, 0.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.app_name), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                        StatusChip(TmStatus.Offline) // no pairing layer yet (Phase 4): state is truthful
                    }
                    OutlinedTextField(query, { vm.query.value = it }, singleLine = true, placeholder = { Text(stringResource(R.string.search_hint)) },
                        leadingIcon = { Icon(Icons.Filled.Search, null) }, modifier = Modifier.fillMaxWidth().padding(16.dp, 8.dp))
                    FilterRow(kind, { vm.kind.value = it }, sort, { vm.sort.value = it })
                    ItemList(home, if (query.isBlank() && kind == null) R.string.items_empty else R.string.no_results, acts, Modifier.weight(1f))
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
                tab == 2 -> ItemList(favs, R.string.favorites_empty, acts, Modifier.weight(1f))
                else -> MoreScreen(Formatter.formatShortFileSize(ctx, total)) { showTrash = true }
            }
        }
    }

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
private fun FilterRow(kind: ItemKind?, onKind: (ItemKind?) -> Unit, sort: SortBy, onSort: (SortBy) -> Unit) {
    var menu by remember { mutableStateOf(false) }
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(start = 16.dp)) {
        LazyRow(Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            item { FilterChip(kind == null, { onKind(null) }, { Text(stringResource(R.string.filter_all)) }) }
            items(ItemKind.entries) { k -> FilterChip(kind == k, { onKind(if (kind == k) null else k) }, { Text(stringResource(kindLabel(k.name))) }) }
        }
        Box {
            TextButton({ menu = true }) { Text(stringResource(R.string.sort)) }
            DropdownMenu(menu, { menu = false }) {
                SortBy.entries.forEach { s ->
                    DropdownMenuItem({ Text((if (s == sort) "✓ " else "") + stringResource(sortLabel(s))) }, { onSort(s); menu = false })
                }
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
private fun MoreScreen(used: String, onTrash: () -> Unit) {
    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(stringResource(R.string.nav_more), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(stringResource(R.string.storage_used, used), fontWeight = FontWeight.SemiBold)
                Text(stringResource(R.string.storage_note), color = TM.Muted)
            }
        }
        OutlinedButton(onTrash, Modifier.fillMaxWidth()) { Icon(Icons.Filled.Delete, null); Spacer(Modifier.width(8.dp)); Text(stringResource(R.string.trash)) }
        Text(stringResource(R.string.version, BuildConfig.VERSION_NAME, BuildConfig.VERSION_CODE), color = TM.Muted)
    }
}
