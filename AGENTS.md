# Sample AGENTS.md file
ignore files that are in gitignore file
use windows 11 powershell commands

be extremely strict with tokens and communicate as little as possible,
significantly reduced my token use,

python scritps should have a ui in dark mode theme

Make sure to look at the editor log for the recent run info
Unity Editor log (Windows): %LOCALAPPDATA%\Unity\Editor\Editor.log
Project shortcut: .\UnityEditorLog.url

we are always going to be on unity 6.4+, don't include paths for older unity versions

CRITICAL: stick to the philosophy that less code is better:
- encapsulate small pieces of code into functions,
- optimize recurring editor workflows for human memory: 
- prefer one primary end-to-end action and at most one cleaning variant
- add debug logs to values to eschew assumptions, 
- dont just fix symptoms, instead look for the root cause,
- do not explain or reiterate analysis, just work on the code,
- follow GC guide here GarbageCollectionInUnity.md,

Problem Solving:
follow this logical pattern - data leads to conjecture which gets tested through a critism which should result in new data
problem solving works by evaluting one example first, finding the critical points of failure, changing underlying assumtions, reevaluating the example, after one example passes all requirements, move to attempt batch evalution for all similar examples
upon the next failure if there is one we isloate the failing example and repeat this problem solving process in order to apply a holistic and abstract solution but one step at a time
prefer fixing the shared underlying contract over patching individual call sites
after isolating one passing example, search for all equivalent codepaths and unify them
if two failures differ only cosmetically, prove whether they share the same resolver, cache, readiness, or staging contract before implementing separate fixes
follow editor documention here C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Data\Documentation\en
treat repeated regressions as evidence of a broken shared contract, not isolated bugs
after fixing one concrete example, always search for sibling codepaths and unify the mechanism
prefer abstractions at the resolver/cache/readiness boundary over per-feature patches
if two fixes touch adjacent systems, stop and propose the common invariant they should share
only accept local fixes when you can explain why the issue is truly isolated

current objective - 
use the Docs\PerformanceGoal.md to track progress
fix loading pipeline to be consistent and fast
