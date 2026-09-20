# ADR 0021: internally managed bundled WebView2 extensions

Status: accepted for initial support, 2026-09-20.

The user requested bundled uBO Lite support without an extension store, new UI,
or a redesign of Zenith policy. The earlier isolated experiment demonstrated
useful MV3 support, but production initialization still disabled extensions.

Use WebView2's supported environment/profile extension APIs in Zenith.App. Pin the
official Edge package and verify its archive/unpacked sources before environment
creation. Install only that path before browsing becomes ready. Retain the default
shared profile and all existing Core/native enforcement. Installation failure
keeps browsing unavailable. Package updates travel with reviewed application
releases; there is no runtime extension download/update service.

This deliberately adds upstream executable content filtering as a trusted
dependency. The existing data-only Ghostery list-update contract remains unchanged;
it does not describe the bundled extension's scriptlets or resource replacements.
uBO configuration and cleanup decisions never become Core policy decisions.

Retain existing filtering during this focused integration. Removing duplicate
filtering, adding extension settings, importing lists, or building an updater
requires a separate change. Full interception of extension background traffic is
not promised by WebView2 and is not introduced as a new policy guarantee.

Implementation, storage, package provenance, limitations and verification belong
in [browser-extensions.md](../browser-extensions.md).
