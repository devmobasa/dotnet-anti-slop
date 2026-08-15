# Contributing

Thanks for helping improve dotnet-anti-slop. Bug reports should include the
diagnostic ID, analyzer version or commit, selected profile, .NET SDK version,
and a minimal source example when possible. Submit security reports through
GitHub's private vulnerability reporting instead of a public issue.

## Changes

1. Add or change the semantic implementation under `src/`.
2. Add a positive test and at least one realistic false-positive boundary under
   `tests/`.
3. Update `rules/rules.json` and the corresponding page in `docs/rules/`.
4. If canonical installer assets changed, run
   `./eng/sync_skill_assets.sh` and review the synchronized skill diff.
5. Run `./eng/validate.sh` and any affected distribution smoke tests under
   `eng/`.

A rule should be automated only when its signal is strong enough to survive
normal application code. Context-dependent advice belongs in the review
playbooks, not in a noisy diagnostic. New rules require a stable ID, a precise
message, examples, configurable severity, and an explicit limitations section.

Keep pull requests focused. Preserve public diagnostic IDs and behavior unless
the change explicitly documents a compatibility break. Do not edit synchronized
files under `skills/install-dotnet-anti-slop/assets/` directly; change their
canonical source and run the synchronization command instead.
