# Sample AGENTS.md file
ignore files that are in gitignore file
use windows 11 powershell commands

Make sure to look at the editor log for the recent run info
Unity Editor log (Windows): %LOCALAPPDATA%\Unity\Editor\Editor.log
Project shortcut: .\UnityEditorLog.url

we are always going to be on unity 6.4+, don't include paths for older unity versions

CRITICAL: stick to the philosophy that less code is better:
- be extremely strict with tokens and communicate as little as possible. Explain NOTHING unless explicitly asked.
- encapsulate small pieces of code into functions,
- optimize recurring editor workflows for human memory: 
- prefer one primary end-to-end action and at most one cleaning variant
- add debug logs to values to eschew assumptions, 
- dont just fix symptoms, instead look for the root cause,
- do not explain or reiterate analysis, just work on the code,
- follow GC guide here GarbageCollectionInUnity.md,

Problem Solving:
follow this logical pattern - data leads to conjecture which gets tested through a critcism which should result in new data
problem solving works by evaluating one example first, finding the critical points of failure, changing underlying assumptions, reevaluating the example, after one example passes all requirements, move to attempt batch evaluation for all similar examples
upon the next failure if there is one we isolate the failing example and repeat this problem solving process in order to apply a holistic and abstract solution but one step at a time
prefer fixing the shared underlying contract over patching individual call sites
after isolating one passing example, search for all equivalent code paths and unify them
if two failures differ only cosmetically, prove whether they share the same resolver, cache, readiness, or staging contract before implementing separate fixes
follow editor documention here C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Data\Documentation\en
treat repeated regressions as evidence of a broken shared contract, not isolated bugs
after fixing one concrete example, always search for sibling code paths and unify the mechanism
prefer abstractions at the resolver/cache/readiness boundary over per-feature patches
if two fixes touch adjacent systems, stop and propose the common invariant they should share
only accept local fixes when you can explain why the issue is truly isolated
you have to treat projects like you own them and can change anything you want

CRITICALLY IMPORTANT MANDATE: code line by line, do not put complex solution into one line
don't over think, use simple logic

current objective - 
use the Docs\PerformanceGoal.md to track progress
fix loading pipeline to be consistent and fast
