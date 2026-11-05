package com.belalmomani.twinmemory

import com.belalmomani.twinmemory.data.db.FolderEntity
import com.belalmomani.twinmemory.data.subtreeIds
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FolderTreeTest {
    private fun f(id: String, parent: String?) = FolderEntity(id, id, parent, 0)

    @Test fun subtreeContainsItselfAndAllDescendants() {
        val all = listOf(f("a", null), f("b", "a"), f("c", "b"), f("x", null), f("y", "x"))
        assertEquals(setOf("a", "b", "c"), subtreeIds("a", all))
        assertEquals(setOf("x", "y"), subtreeIds("x", all))
        assertEquals(setOf("c"), subtreeIds("c", all))
    }

    @Test fun movingIntoOwnSubtreeIsDetectable() {
        val all = listOf(f("a", null), f("b", "a"))
        val sub = subtreeIds("a", all)
        assertTrue("b" in sub)      // target inside the folder's own subtree must be rejected
        assertFalse("x" in sub)
    }

    @Test fun orphansAndCyclesDoNotHang() {
        val all = listOf(f("a", "b"), f("b", "a"))   // corrupted data: a cycle
        assertEquals(setOf("a", "b"), subtreeIds("a", all))
    }
}
