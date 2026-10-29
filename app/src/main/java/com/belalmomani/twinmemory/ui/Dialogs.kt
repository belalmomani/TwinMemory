package com.belalmomani.twinmemory.ui

import android.text.format.Formatter
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.data.db.FolderEntity
import com.belalmomani.twinmemory.data.db.ItemEntity
import java.text.DateFormat
import java.util.Date

@Composable
fun TextInputDialog(title: Int, label: Int, initial: String = "", multiline: Boolean = false, confirm: Int = R.string.save, onDismiss: () -> Unit, onOk: (String) -> Unit) {
    var v by remember { mutableStateOf(initial) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(title)) },
        text = { OutlinedTextField(v, { v = it }, label = { Text(stringResource(label)) }, minLines = if (multiline) 4 else 1, maxLines = if (multiline) 10 else 1, modifier = Modifier.fillMaxWidth()) },
        confirmButton = { TextButton({ if (v.isNotBlank()) { onOk(v); onDismiss() } }) { Text(stringResource(confirm)) } },
        dismissButton = { TextButton(onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

@Composable
fun SaveChoiceDialog(onText: () -> Unit, onFile: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.save_item)) },
        text = {
            Column {
                TextButton({ onDismiss(); onText() }, Modifier.fillMaxWidth()) { Text(stringResource(R.string.save_text_link)) }
                TextButton({ onDismiss(); onFile() }, Modifier.fillMaxWidth()) { Text(stringResource(R.string.import_file)) }
            }
        },
        confirmButton = {}, dismissButton = { TextButton(onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

@Composable
fun MoveDialog(folders: List<FolderEntity>, onPick: (String?) -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.move_to)) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                TextButton({ onPick(null); onDismiss() }, Modifier.fillMaxWidth()) { Text(stringResource(R.string.no_folder)) }
                folders.forEach { f -> TextButton({ onPick(f.id); onDismiss() }, Modifier.fillMaxWidth()) { Text(f.name) } }
            }
        },
        confirmButton = {}, dismissButton = { TextButton(onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

@Composable
fun ConfirmDialog(title: Int, message: Int, confirm: Int, onDismiss: () -> Unit, onOk: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(title)) }, text = { Text(stringResource(message)) },
        confirmButton = { TextButton({ onOk(); onDismiss() }) { Text(stringResource(confirm)) } },
        dismissButton = { TextButton(onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun PropertiesDialog(item: ItemEntity, tags: List<String>, onOpen: () -> Unit, onAddTag: (String) -> Unit, onRemoveTag: (String) -> Unit, onDismiss: () -> Unit) {
    val ctx = LocalContext.current
    var tag by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(item.title, maxLines = 2) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                if (item.text != null) Text(item.text)
                Text(stringResource(R.string.prop_type) + ": " + stringResource(kindLabel(item.kind)))
                Text(stringResource(R.string.prop_source) + ": " + stringResource(deviceLabel(item.sourceDevice)))
                Text(stringResource(R.string.prop_size) + ": " + Formatter.formatShortFileSize(ctx, item.sizeBytes))
                Text(stringResource(R.string.prop_date) + ": " + DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT).format(Date(item.createdAt)))
                Text(stringResource(R.string.tags))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    tags.forEach { t -> AssistChip({ onRemoveTag(t) }, { Text("$t  ✕") }) }
                }
                Row(verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
                    OutlinedTextField(tag, { tag = it }, singleLine = true, label = { Text(stringResource(R.string.add_tag)) }, modifier = Modifier.weight(1f))
                    TextButton({ onAddTag(tag); tag = "" }) { Text(stringResource(R.string.add)) }
                }
            }
        },
        confirmButton = {
            if (item.kind != "TEXT") TextButton(onOpen) { Text(stringResource(R.string.open)) }
        },
        dismissButton = { TextButton(onDismiss) { Text(stringResource(R.string.close)) } },
    )
}
