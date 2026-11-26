using System.Collections.Generic;

namespace ApkShellext2 {
    /// <summary>
    /// What a package looks like to the shell at a glance: nothing structural stands
    /// out, something marks it as repacked or debuggable, or the container contradicts
    /// its own manifest or resource table.
    ///
    /// This is a hint for the user, not a security guarantee. The readers only look at
    /// the structure the container itself carries; no signature chain is verified and
    /// no reputation feed is consulted, so a package graded Info can still be hostile.
    /// </summary>
    public enum PackageVerdict {
        /// <summary>No structural evidence of repacking or tampering.</summary>
        Info,
        /// <summary>A repacking marker or a debuggable manifest.</summary>
        Suspicious,
        /// <summary>The container contradicts what its own manifest and table say.</summary>
        Tampered
    }

    /// <summary>
    /// The structural facts the verdict is graded from. A reader fills them in from what
    /// it already parsed, which keeps <see cref="PackageVerdictRules"/> a pure function
    /// of these flags and testable without a package file.
    /// </summary>
    public sealed class PackageVerdictEvidence {
        /// <summary>
        /// The manifest names the icon by resource id, but the package carries no
        /// resources.arsc, so there is no table the reference could ever resolve against.
        /// </summary>
        public bool IconReferenceWithoutResourceTable { get; set; }

        /// <summary>The resolved icon path is not an entry of the archive.</summary>
        public bool IconEntryMissing { get; set; }

        /// <summary>Packer named by the APK signing block; empty when none of the known tools.</summary>
        public string Packer { get; set; }

        /// <summary>The manifest explicitly marks the package debuggable.</summary>
        public bool Debuggable { get; set; }
    }

    /// <summary>
    /// Turns the evidence into one verdict and one line of text. Keeping the grading in
    /// one place means the CSV, the future infotip template and the tests agree on what
    /// a fact means, and the order the reasons are listed in cannot drift per caller.
    /// </summary>
    public static class PackageVerdictRules {
        /// <summary>
        /// The evidence text never grows past three items: the user reads one line, and a
        /// longer list would be truncated by the shell anyway.
        /// </summary>
        public const int MaxEvidenceItems = 3;

        /// <summary>
        /// Grades the evidence and renders the user-facing line. The order is fixed: the
        /// tampered facts come first because they name a contradiction inside the
        /// package, then the suspicious markers, so trimming to three keeps the strongest
        /// reason. The line is empty exactly when the verdict is Info.
        /// </summary>
        public static PackageVerdict Grade(PackageVerdictEvidence evidence, out string text) {
            text = "";
            if (evidence == null) return PackageVerdict.Info;

            var items = new List<string>(5);
            bool tampered = false;
            if (evidence.IconReferenceWithoutResourceTable) {
                tampered = true;
                items.Add("resources.arsc is missing");
            }
            if (evidence.IconEntryMissing) {
                tampered = true;
                items.Add("the icon the manifest names is not in the package");
            }
            if (!string.IsNullOrEmpty(evidence.Packer))
                items.Add("signed by " + evidence.Packer);
            if (evidence.Debuggable)
                items.Add("the manifest marks the package debuggable");

            if (items.Count > MaxEvidenceItems)
                items.RemoveRange(MaxEvidenceItems, items.Count - MaxEvidenceItems);
            if (items.Count == 0) return PackageVerdict.Info;

            text = string.Join("; ", items);
            return tampered ? PackageVerdict.Tampered : PackageVerdict.Suspicious;
        }
    }
}
