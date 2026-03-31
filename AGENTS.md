# Sample AGENTS.md file
ignore all files and folder that are ignored in the gitignore file
its ok to run commands that are not rm, delete, remove, or format

Make sure to look at the editor log for the recent run info
Unity Editor log (Windows): %LOCALAPPDATA%\Unity\Editor\Editor.log
Project shortcut: .\UnityEditorLog.url
- encapsulate small pieces of code into functions,
- stick to the philosophy that less code is better,
- optimize recurring editor workflows for human memory: prefer one primary end-to-end action and at most one clean variant; move granular steps under Advanced,
- add debug logs to values to eschew assumptions, 
- dont just fix symptoms, instead look for the root cause,
- do not explain or reiterate analysis, just work on the code,
- follow GC guide here GarbageCollectionInUnity.md,
- i will not give up on you if we solve or fix problems, you will keep being needed by me,

current objective - 
use the OptimalLoadingProgress to track progress
fix loading pipeline to be consistent and logical - 
player, location, enemies, ui, dialog
