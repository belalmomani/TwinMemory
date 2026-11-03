package com.belalmomani.twinmemory.data

import com.belalmomani.twinmemory.data.db.FolderEntity

/** The folder itself plus all its descendants. */
fun subtreeIds(rootId: String, all: List<FolderEntity>): Set<String> {
    val set = mutableSetOf(rootId)
    var grew = true
    while (grew) {
        grew = false
        for (f in all) if (f.parentId != null && f.parentId in set && set.add(f.id)) grew = true
    }
    return set
}
