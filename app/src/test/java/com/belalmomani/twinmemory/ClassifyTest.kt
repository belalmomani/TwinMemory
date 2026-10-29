package com.belalmomani.twinmemory

import com.belalmomani.twinmemory.domain.Classify
import com.belalmomani.twinmemory.domain.ItemKind
import org.junit.Assert.*
import org.junit.Test

class ClassifyTest {
    @Test fun mimeKinds() {
        assertEquals(ItemKind.IMAGE, Classify.kindFromMime("image/png", "a.png"))
        assertEquals(ItemKind.VIDEO, Classify.kindFromMime("video/mp4", null))
        assertEquals(ItemKind.DOCUMENT, Classify.kindFromMime("application/pdf", "x.pdf"))
        assertEquals(ItemKind.DOCUMENT, Classify.kindFromMime(null, "notes.txt"))
        assertEquals(ItemKind.FILE, Classify.kindFromMime("application/zip", "a.zip"))
    }
    @Test fun urls() {
        assertTrue(Classify.isSingleUrl(" https://example.com/a?b=1 "))
        assertFalse(Classify.isSingleUrl("see https://example.com"))
        assertFalse(Classify.isSingleUrl("example.com"))
        assertEquals("example.com", Classify.hostOf("https://example.com/a?b=1"))
    }
    @Test fun sanitize() {
        assertEquals("passwd", Classify.sanitizeFileName("../../passwd").substringAfterLast('_'))
        assertFalse(Classify.sanitizeFileName("../../x").contains('/'))
        assertEquals("file", Classify.sanitizeFileName("..."))
        assertEquals("file", Classify.sanitizeFileName(null))
    }
    @Test fun like() {
        assertNull(Classify.likePattern("  "))
        assertEquals("%50\\%\\_a%", Classify.likePattern("50%_a"))
    }
    @Test fun title() { assertEquals("hello", Classify.titleFor("\n  hello \nworld")) }
}
