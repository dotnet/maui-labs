# Human-controlled repair draft publication

Experimental policy increment; off by default and **not-qualified**.

The tooling plugin's optional `maui-devflow-ci-publish` skill separates Git/PR
publication from test execution and repair. It requires an explicit exact-scope
human request and an already verified local change. It cannot run a test,
request a broker grant, change a selector, modify source, or manufacture
missing oracle/cleanup evidence. It has no automatic workflow trigger or
bundled CLI installation.

This adapts the useful publication checks in fork `4b8f833e` without enabling
that fork's automatic post-run publication default. The two are intentionally
different policies, not synonymous implementations.

The human reviewer owns the draft PR. No force push, automatic merge, automatic
issue closure, ready-for-review transition or qualification claim is allowed.
Demo repairs remain draft and do-not-merge because merging them disables the
deliberately failing demonstration.

This branch can be reviewed independently of the initial Goal 4 execution and
CI-evidence foundation. Its evaluation scenarios are review fixtures, not
evidence that any live repair or publication workflow was executed.
