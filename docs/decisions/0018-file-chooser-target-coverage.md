# ADR 0018: File chooser enforcement across renderer targets

Date: 2026-09-14

Status: Accepted F06 correction

Root-target chooser interception did not apply to out-of-process iframe targets.
Use WebView2's CDP session transport to configure chooser interception in each
auto-attached iframe target before it resumes, recursively covering descendants.
`FileChooserGuard` owns this lifecycle; `BrowserCapabilityPolicy` remains the
unchanged decision authority. Core currently denies all file selection.

No dedicated HTML file-chooser event exists in the installed WebView2 frame API.
Root/frame navigation events are not substitutes, and post-hoc Windows dialog
closure or JavaScript interception would not establish the intended boundary.
Retain Chromium's native chooser for an explicit allowed decision, without a
host-provided file or replacement chooser.

Unknown/unpaused new targets and required setup/protocol failures have no allow
fallback. Paused targets resume only after chooser policy and recursive attachment
are configured; failure invokes controller disposal. Frame destruction and process
transitions require lifecycle regression coverage. This is a fixed-policy adapter,
not a new grant/revocation system.

See [F06 validation](../f06-file-chooser-validation.md) for the implementation,
API sources, native test matrix, independent observations and compatibility limits.
The original ADR 0015 evidence remains historical; its root-only chooser coverage
is superseded by this correction. Other capability controls are unchanged.
