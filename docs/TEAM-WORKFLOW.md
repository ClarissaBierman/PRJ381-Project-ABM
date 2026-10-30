# Working as a team

How our group worked on this project, and what we'd recommend you keep doing.

## Split the work into bundles

We split the project into "bundles", each with **one owner**: for example the engine, the config module, the dashboard grid, the database, Compare. The owner is responsible for their bundle being done and working, and is the first person to ask about it. Others can still help.

The project is split into separate C# projects (see [ARCHITECTURE.md](ARCHITECTURE.md)), so bundles rarely touch the same files.

**Agree on the shared pieces first.** Before splitting up, agree on the parts everyone uses: `SimulationConfig`, `Agent`, `TickStatistics` and the SignalR message names. Changing those later breaks everyone else's work.

## Branches and pull requests (GitHub Desktop)

Never work directly on `main`. `main` should always build and pass all tests.

1. **Get the latest main:** in GitHub Desktop, switch to `main` (Current branch) and click **Fetch origin**, then **Pull origin**.
2. **Make a branch:** **Branch > New branch**, name it after what you're doing, for example `feature/fire-model` or `fix/boids-edges`. Base it on `main`.
3. **Work and commit often:** make a change, check it builds and the tests pass, then in GitHub Desktop write a short summary ("Add Fire agent and its tests") and click **Commit**. Small commits are easier to review and to undo.
4. **Push:** click **Publish branch** (first time) or **Push origin**.
5. **Open a pull request:** click **Create Pull Request**. On GitHub, describe what you changed and how you tested it. Ask a teammate to review.
6. **Review:** the reviewer reads the changes, pulls the branch and tries it. Fix what they find on the same branch.
7. **Merge:** once approved, click **Merge pull request** on GitHub. Then everyone pulls `main` again.

**Keep pull requests small.** One feature or fix each. A pull request touching 40 files takes days to review and is much more likely to clash with someone else's work. Ours that merged easily were the small ones.

### When main has moved on while you were working

In GitHub Desktop, on your branch: **Branch > Update from main**. If there are conflicts, GitHub Desktop shows the files. Open each one in Visual Studio, keep the right lines, remove the `<<<<<<<`, `=======` and `>>>>>>>` markers, build, test, then commit.

## Before you open a pull request

- [ ] It builds with no new warnings.
- [ ] All tests pass, and you added tests for new logic.
- [ ] You ran the dashboard and tried what you changed.
- [ ] New classes and methods have a `/// <summary>` and the file has a header comment, like the rest of the code.
- [ ] No personal paths (like `C:\Users\yourname\...`) in code or settings files.

## If someone is stuck or away

When a bundle's owner can't continue, hand it over explicitly: the new owner reads the code, asks questions, and the team knows who owns it now. That way nothing stalls.
