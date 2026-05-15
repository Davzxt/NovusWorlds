Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
launcher = scriptDir & "\launch-hidden.ps1"
uri = ""
If WScript.Arguments.Count > 0 Then uri = WScript.Arguments(0)
cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Chr(34) & launcher & Chr(34) & " " & Chr(34) & uri & Chr(34)
shell.CurrentDirectory = scriptDir
shell.Run cmd, 0, False
