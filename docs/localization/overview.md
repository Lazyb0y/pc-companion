# Localization

## Languages

- English is the source language and the fallback for missing strings.
- Translations: Arabic (`ar`), Bangla (`bn`), Chinese (Simplified) (`zh-Hans`), French (`fr`), German (`de`) and Spanish (`es`).
- Translations drafted by an AI agent have the comment "Needs native review." on each string until a native speaker reviews them.
- By default the app uses the Windows display language. The `general.language` setting chooses another one.
- The settings app applies the language when it starts; the host applies a change right away.

## Strings

- English strings are in `src/PCCompanion.Localization/Strings.resx`; each has a comment saying where it appears. Translations are in `Strings.<language>.resx`, named with neutral BCP-47 tags.
- MSBuild generates the `Strings` class from `Strings.resx` at build time.
- In code: `Strings.NavigationHome`. In XAML: `{app:Localized Key=NavigationHome}`.
- No user-facing text in code or XAML; every string comes from the resources. The product name, `AppDisplayName`, is a resource too and stays in English unless the product is known by another name in the language.
- To add a language, add `Strings.<language>.resx` with every string. No code changes: the list of languages comes from the resource folders the build creates.

## Plurals

- A string that changes with a number has one entry per CLDR plural category its language uses: `GeneralLogsRetention_one`, `GeneralLogsRetention_other` and so on. English uses `one` and `other`; Arabic uses `zero`, `one`, `two`, `few`, `many` and `other`; Chinese uses only `other`.
- `PluralStrings.Format("GeneralLogsRetention", count)` picks the form with the plural rules of the ICU library built into Windows (`icu.dll`), and formats the number with the user's regional settings.
- The rules are those of the language the string comes from: a language without a translation shows English, with English rules.
- The `other` form contains every placeholder. Other forms may leave out the number, as in Arabic "two days", which is one word.

## Right to left

- The window and the tray menu are laid out right to left when the language their strings come from is written right to left. A right-to-left language without a translation shows English, left to right.

## Tests

`PCCompanion.Localization.Tests` checks every language: the same strings as English, exactly the plural forms the language uses, the same placeholders, and no empty strings.
