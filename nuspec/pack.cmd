@echo off
del *.nupkg
nuget pack Acr.SpeechRecognition.nuspec
nuget pack Acr.SpeechDialogs.nuspec
pause