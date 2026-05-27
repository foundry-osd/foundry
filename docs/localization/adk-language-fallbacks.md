# ADK Language Fallbacks

This document defines the first-pass UI culture fallback policy for Foundry OSD, Foundry Connect, and Foundry Deploy when expanding resource files to match the Windows ADK WinPE optional component languages.

The installed ADK source inspected for this list is:

```text
C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Windows Preinstallation Environment\amd64\WinPE_OCs
```

## Goals

- Keep exact culture matches deterministic.
- Avoid exposing a culture in the UI unless that culture has a resource file.
- Avoid relying on the current first same-language match when multiple ADK cultures share the same language family.
- Fall back to `en-US` only when no supported culture is a reasonable language-family match.

## Current Behavior

The shared localization catalog currently supports only:

```text
en-US
fr-FR
```

With this catalog:

- `fr-CA`, `fr-BE`, `fr-CH`, and neutral `fr` resolve to `fr-FR`.
- `en-GB`, `en-CA`, and neutral `en` resolve to `en-US`.
- Unsupported language families such as `de-DE` or `es-ES` resolve to `en-US`.

This is implemented by exact match first, then a two-letter language-family match. That rule is acceptable with one culture per language, but it is not enough once multiple variants exist for the same language.

## ADK Cultures

The installed ADK currently exposes 38 WinPE cultures:

```text
ar-SA
bg-BG
cs-CZ
da-DK
de-DE
el-GR
en-GB
en-US
es-ES
es-MX
et-EE
fi-FI
fr-CA
fr-FR
he-IL
hr-HR
hu-HU
it-IT
ja-JP
ko-KR
lt-LT
lv-LV
nb-NO
nl-NL
pl-PL
pt-BR
pt-PT
ro-RO
ru-RU
sk-SK
sl-SI
sr-Latn-RS
sv-SE
th-TH
tr-TR
uk-UA
zh-CN
zh-TW
```

## Exact Culture Mapping

Every ADK culture should resolve to itself when the same culture is supported by the app:

| Requested culture | Exact fallback target |
| --- | --- |
| `ar-SA` | `ar-SA` |
| `bg-BG` | `bg-BG` |
| `cs-CZ` | `cs-CZ` |
| `da-DK` | `da-DK` |
| `de-DE` | `de-DE` |
| `el-GR` | `el-GR` |
| `en-GB` | `en-GB` |
| `en-US` | `en-US` |
| `es-ES` | `es-ES` |
| `es-MX` | `es-MX` |
| `et-EE` | `et-EE` |
| `fi-FI` | `fi-FI` |
| `fr-CA` | `fr-CA` |
| `fr-FR` | `fr-FR` |
| `he-IL` | `he-IL` |
| `hr-HR` | `hr-HR` |
| `hu-HU` | `hu-HU` |
| `it-IT` | `it-IT` |
| `ja-JP` | `ja-JP` |
| `ko-KR` | `ko-KR` |
| `lt-LT` | `lt-LT` |
| `lv-LV` | `lv-LV` |
| `nb-NO` | `nb-NO` |
| `nl-NL` | `nl-NL` |
| `pl-PL` | `pl-PL` |
| `pt-BR` | `pt-BR` |
| `pt-PT` | `pt-PT` |
| `ro-RO` | `ro-RO` |
| `ru-RU` | `ru-RU` |
| `sk-SK` | `sk-SK` |
| `sl-SI` | `sl-SI` |
| `sr-Latn-RS` | `sr-Latn-RS` |
| `sv-SE` | `sv-SE` |
| `th-TH` | `th-TH` |
| `tr-TR` | `tr-TR` |
| `uk-UA` | `uk-UA` |
| `zh-CN` | `zh-CN` |
| `zh-TW` | `zh-TW` |

## Language Family Fallback Defaults

For unsupported variants inside a language family, use the following default culture.

| Language family | Default fallback | Notes |
| --- | --- | --- |
| `ar` | `ar-SA` | Only Arabic ADK culture available. |
| `bg` | `bg-BG` | Only Bulgarian ADK culture available. |
| `cs` | `cs-CZ` | Only Czech ADK culture available. |
| `da` | `da-DK` | Only Danish ADK culture available. |
| `de` | `de-DE` | Use Germany as the default German fallback. |
| `el` | `el-GR` | Only Greek ADK culture available. |
| `en` | `en-US` | Keep `en-US` as the neutral/default English fallback. Exact `en-GB` still resolves to `en-GB`. |
| `es` | `es-ES` | Use `es-ES` as neutral Spanish; see regional overrides for Latin America. |
| `et` | `et-EE` | Only Estonian ADK culture available. |
| `fi` | `fi-FI` | Only Finnish ADK culture available. |
| `fr` | `fr-FR` | Use `fr-FR` as neutral French; exact `fr-CA` still resolves to `fr-CA`. |
| `he` | `he-IL` | Only Hebrew ADK culture available. |
| `hr` | `hr-HR` | Only Croatian ADK culture available. |
| `hu` | `hu-HU` | Only Hungarian ADK culture available. |
| `it` | `it-IT` | Only Italian ADK culture available. |
| `ja` | `ja-JP` | Only Japanese ADK culture available. |
| `ko` | `ko-KR` | Only Korean ADK culture available. |
| `lt` | `lt-LT` | Only Lithuanian ADK culture available. |
| `lv` | `lv-LV` | Only Latvian ADK culture available. |
| `nb` | `nb-NO` | Norwegian Bokmal. Also map legacy `no` to `nb-NO`. |
| `nl` | `nl-NL` | Use Netherlands as the default Dutch fallback. |
| `pl` | `pl-PL` | Only Polish ADK culture available. |
| `pt` | `pt-PT` | Use Portugal as neutral Portuguese; see regional override for Brazil. |
| `ro` | `ro-RO` | Only Romanian ADK culture available. |
| `ru` | `ru-RU` | Only Russian ADK culture available. |
| `sk` | `sk-SK` | Only Slovak ADK culture available. |
| `sl` | `sl-SI` | Only Slovenian ADK culture available. |
| `sr` | `sr-Latn-RS` | Only Serbian ADK culture available, Latin script. |
| `sv` | `sv-SE` | Use Sweden as the default Swedish fallback. |
| `th` | `th-TH` | Only Thai ADK culture available. |
| `tr` | `tr-TR` | Only Turkish ADK culture available. |
| `uk` | `uk-UA` | Only Ukrainian ADK culture available. |
| `zh` | `zh-CN` | Use simplified Chinese as neutral `zh`; see script and region overrides. |

## Regional Overrides

Some languages need more than a two-letter fallback because the ADK includes multiple regional or script variants.

| Requested culture pattern | Fallback target | Rationale |
| --- | --- | --- |
| `en-*` except exact `en-GB` | `en-US` | Product source language and default culture. |
| `es-MX` | `es-MX` | Exact ADK culture. |
| `es-AR`, `es-BO`, `es-CL`, `es-CO`, `es-CR`, `es-CU`, `es-DO`, `es-EC`, `es-GT`, `es-HN`, `es-NI`, `es-PA`, `es-PE`, `es-PR`, `es-PY`, `es-SV`, `es-US`, `es-UY`, `es-VE`, `es-419` | `es-MX` | Prefer the ADK Latin American Spanish variant when the requested region is in the Americas. |
| `es-*` not listed above | `es-ES` | Default Spanish fallback. |
| `fr-CA` | `fr-CA` | Exact ADK culture. |
| `fr-*` except exact `fr-CA` | `fr-FR` | Default French fallback. |
| `pt-BR` | `pt-BR` | Exact ADK culture. |
| `pt-*` except exact `pt-BR` | `pt-PT` | Default Portuguese fallback for Portugal and other Portuguese-speaking regions. |
| `zh-Hans`, `zh-CN`, `zh-SG` | `zh-CN` | Simplified Chinese fallback. |
| `zh-Hant`, `zh-TW`, `zh-HK`, `zh-MO` | `zh-TW` | Traditional Chinese fallback. |
| `zh-*` without script or recognized region | `zh-CN` | Default Chinese fallback. |
| `sr-Latn-*`, `sr-RS`, neutral `sr` | `sr-Latn-RS` | Only Serbian ADK culture is Latin script. |
| `sr-Cyrl-*` | `sr-Latn-RS` | No Cyrillic Serbian ADK culture is available; this is a script fallback and should be documented in UI/testing. |
| `no`, `no-NO`, `nb-*` | `nb-NO` | Map legacy Norwegian and Bokmal variants to the ADK Bokmal culture. |
| `nn-*` | `nb-NO` | No Nynorsk ADK culture is available; this is a language variant fallback. |

## Implementation Notes

The current fallback method picks the first supported culture with the same two-letter language. That becomes order-dependent once we add:

```text
en-GB / en-US
es-ES / es-MX
fr-CA / fr-FR
pt-BR / pt-PT
zh-CN / zh-TW
```

Before exposing all ADK cultures in the UI, replace the implicit first-match family fallback with an explicit fallback policy:

1. Try exact supported culture match.
2. Try explicit regional override.
3. Try language family default.
4. Fall back to `en-US`.

This policy should be shared by Foundry OSD, Foundry Connect, and Foundry Deploy through `Foundry.Localization`.

## Resource Exposure Policy

Creating ADK culture resource files is separate from exposing those cultures in the language picker.

Recommended first step:

- Create the ADK culture resource files for coverage.
- Keep `FoundrySupportedCultures` limited to cultures with real translated values.
- Add cultures to `FoundrySupportedCultures` only when their resource files are translated or intentionally approved for English fallback.

