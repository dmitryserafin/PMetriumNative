@echo off

adb -s %1 shell "logcat -d | grep PERFORMANCE"