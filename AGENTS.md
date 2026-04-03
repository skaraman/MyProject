# Sample AGENTS.md file
ignore all files and folder that are ignored in the gitignore file
its ok to run commands that are not rm, delete, remove, or format

python scritps should have a ui in dark mode theme

Make sure to look at the editor log for the recent run info
Unity Editor log (Windows): %LOCALAPPDATA%\Unity\Editor\Editor.log
Project shortcut: .\UnityEditorLog.url
- encapsulate small pieces of code into functions,
- stick to the philosophy that less code is better,
- optimize recurring editor workflows for human memory: prefer one primary end-to-end action and at most one clean variant; remove granular steps from Advanced that never need to be run indipendently,
- add debug logs to values to eschew assumptions, 
- dont just fix symptoms, instead look for the root cause,
- do not explain or reiterate analysis, just work on the code,
- follow GC guide here GarbageCollectionInUnity.md,

follow this logical pattern - data leads to conjecture which gets tested through a critism which should result in new data
problem solving works by evaluting one example first, finding the critical points of failure, changing underlying assumtions, reevaluating the example, after one example passes all requirements, move to attempt batch evalution for all similar examples, upon the next failure if there is one we isloate the failing example and repeat this problem solving process in order to apply a holistic and abstract solution but one step at a time
follow editor documention here C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Data\Documentation\en


current objective - 
use the Docs\PerformanceGoal.md to track progress
fix loading pipeline to be consistent and fast
