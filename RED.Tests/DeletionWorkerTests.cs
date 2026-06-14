using System;
using System.Collections.Generic;
using Xunit;

namespace RED
{
    // Locks in the protected-folder invariant: a directory must not be deleted when
    // it (or any ancestor) is the parent of a user-protected folder. Regression guard
    // for the bug where a protected child was destroyed by an empty-eligible ancestor's
    // recursive delete because only the child's own path was checked.
    public class DeletionWorkerTests
    {
        private static HashSet<string> Set(params string[] paths)
        {
            return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Protect_Self_IsProtected()
        {
            Assert.True(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\a\b\keep", Set(@"C:\a\b\keep")));
        }

        [Fact]
        public void Protect_Ancestor_OfProtected_IsProtected()
        {
            var p = Set(@"C:\a\b\keep");
            // Every ancestor of a protected folder must itself be treated as protected,
            // or a recursive delete of the ancestor would destroy the protected child.
            Assert.True(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\a\b", p));
            Assert.True(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\a", p));
            Assert.True(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\a\b\", p)); // trailing separator
        }

        [Fact]
        public void Protect_IsCaseInsensitive()
        {
            var p = Set(@"C:\Data\Keep");
            Assert.True(DeletionWorker.IsProtectedOrAncestorOfProtected(@"c:\data", p));
            Assert.True(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\DATA\KEEP", p));
        }

        [Fact]
        public void Sibling_And_Unrelated_AreNotProtected()
        {
            var p = Set(@"C:\a\b\keep");
            Assert.False(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\a\b\other", p));
            Assert.False(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\x", p));
        }

        [Fact]
        public void PrefixSibling_IsNotMistakenForAncestor()
        {
            // "C:\a\bc" shares a string prefix with "C:\a\b\keep" but is NOT an ancestor;
            // the path-separator boundary must prevent a false protection match.
            Assert.False(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\a\bc", Set(@"C:\a\b\keep")));
        }

        [Fact]
        public void EmptyOrNull_AreNotProtected()
        {
            Assert.False(DeletionWorker.IsProtectedOrAncestorOfProtected(@"C:\a", new HashSet<string>()));
            Assert.False(DeletionWorker.IsProtectedOrAncestorOfProtected(null, Set(@"C:\a")));
            Assert.False(DeletionWorker.IsProtectedOrAncestorOfProtected("", Set(@"C:\a")));
        }
    }
}
