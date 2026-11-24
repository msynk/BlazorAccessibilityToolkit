# IBM Equal Access accessibility checker engine

`ace.js` is the browser bundle of the npm package [accessibility-checker-engine](https://www.npmjs.com/package/accessibility-checker-engine) version 4.0.34, copied unchanged from <https://registry.npmjs.org/accessibility-checker-engine/-/accessibility-checker-engine-4.0.34.tgz>. Source: <https://github.com/IBMa/equal-access>.

- SHA-256 of `ace.js`: `1fd7c18b2aa5ba3ec0444a275e2e08e2823c317316c6613d1c273d2a688760fa`
- Copyright IBM, Inc., licensed under the Apache License, Version 2.0 (`LICENSE-2.0.txt`; the notice of the bundle is `ace.js.LICENSE.txt`).

The toolkit embeds it in BlazorAccessibilityToolkit.Playwright and runs it when `AccessibilityAuditOptions.IbmEqualAccess` is set. To upgrade, replace `ace.js` with the file of a newer package version (published at least two weeks earlier), update `EqualAccessRunner.EngineVersion`, the version and hash above, and follow [docs/upgrading.md](../../../../docs/upgrading.md).
