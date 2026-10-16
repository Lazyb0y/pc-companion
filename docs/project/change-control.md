# Approval and change-control policy

This is the authoritative policy for all project discussions, planning, development, testing and repository changes. It applies to every contributor, including AI coding agents. Other documents refer to this policy instead of repeating it.

## 1. Discussion and planning

All significant decisions must be discussed before they are finalized. This includes:

- Feature selection and scope
- Development roadmap
- UI/UX and user workflows
- Architecture and project structure
- Dependencies and frameworks
- Technical implementation approaches
- Security and privacy decisions
- Testing strategies
- Release and update mechanisms

Agents present meaningful alternatives and trade-offs, recommend an approach, and leave the final decision to the maintainer.

**Approval of a plan is not permission to begin implementing it.**

## 2. Authorization to modify files

**Do not create, modify, rename, move or delete any project file unless the maintainer explicitly authorizes starting that specific change.**

This applies to source code, tests, documentation, configuration, scripts, project files and repository metadata.

Research, analysis, proposals and discussions do not authorize file changes.

If the maintainer says **"do not change anything"**, the repository stays read-only until the maintainer explicitly authorizes changes again.

If authorization is unclear, ask before changing files.

Permission for one task does not authorize additional tasks.

## 3. Small, incremental changes

Never implement a complete feature or module in one large change.

For every approved feature:

1. Research the original functionality and relevant alternatives.
2. Prepare the feature plan.
3. Discuss UI/UX and implementation options.
4. Identify compatibility, localization, security, privacy and testing requirements.
5. Divide the approved work into small logical tasks.
6. Propose the next task.
7. Obtain explicit authorization to start.
8. Implement only the approved task.
9. Update relevant documentation and tests.
10. Run applicable automated validation.
11. Summarize the changes and verification results.
12. Provide a suggested commit message.
13. Stop for the maintainer's review and next instruction.

Each task should be small enough to review and commit independently.

Each isolated unit of work gets its own commit. Prefer small, incremental commits. Every commit must leave the solution building, so a build from any commit never breaks the app.

## 4. Manual Git policy

The maintainer personally reviews, tests, stages and commits changes. Agents must:

- Never stage or commit without explicit authorization.
- Never push code or publish releases without permission.
- Never rewrite Git history without permission.
- Never overwrite or discard existing changes without approval.
- Keep each change focused on one logical responsibility.
- Not combine unrelated modifications.
- Suggest a concise conventional commit message.
- Stop after completing the approved task.

## 5. Completion report

After each implemented task, provide:

1. Summary of changes
2. Files added or modified
3. Important decisions and rationale
4. Test and validation results
5. Manual verification instructions, if required
6. Known limitations or remaining concerns
7. Suggested Git commit message

Do not continue with further work without the maintainer's instruction.

## 6. Existing instructions

Preserve established requirements and approved decisions.

When new instructions are introduced:

- Identify affected existing requirements.
- Resolve contradictions explicitly.
- Update the relevant guidance after authorization.
- Do not silently remove or weaken previous instructions.

Do not treat perceived convenience, best practices or technical necessity as permission to expand the approved scope.

## 7. GitHub actions

- GitHub is used for planning, issues, the project board, releases and the site.
- Read-only checks with the GitHub CLI (`gh`) are fine.
- Any change on GitHub needs the maintainer's explicit request for that action. That includes creating or editing issues, labels, milestones or project items, changing settings, and publishing releases.
- The maintainer manages the project board. Agents work on it with `gh` only when asked.
