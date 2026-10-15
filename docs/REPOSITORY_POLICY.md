# Main branch policy

`main` uses active GitHub branch rulesets:

- Only `@hugo2006alm` can update `main`, including merging pull requests. A separate
  update restriction preserves this even if collaborators receive write access later.
- Changes require a pull request and one code-owner approval. `.github/CODEOWNERS`
  names only `@hugo2006alm` for every file, including CODEOWNERS and workflows.
- Approvals are dismissed when new changes are pushed. Review conversations must be
  resolved before a normal merge.
- The `windows` check from the GitHub Actions app (integration ID 15368) must pass.
  The PR must be up to date with `main` before a normal merge.
- Direct pushes, force pushes, and deletion of `main` are blocked.

Only `@hugo2006alm` has bypass access to the PR/review/CI rules, and only when merging
a pull request. Bypass can override missing approval or failing/pending CI; it is a
deliberate exception, not a successful CI result. GitHub does not allow PR authors
to approve their own PRs, so owner-authored PRs need this option. Do not use direct
pushes or change the rules just to land routine work.

Public readers can submit informational reviews; only the required owner's approval
satisfies the code-owner gate. Repository ownership and ruleset administration remain
with the owner. These settings govern GitHub identities: a CLI or app authorized to
act as the owner uses the owner's permissions.

See [repository rules](https://github.com/hugo2006alm/airpods-switch/rules) for the live
configuration. Local copies of API payloads are not the source of enforcement.
