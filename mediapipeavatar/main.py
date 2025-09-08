# main.py
from body import BodyThread
import time
import global_vars
from sys import exit

thread = BodyThread()
thread.start()

print("실행 중... 종료하려면 q 입력")
while True:
    i = input()
    if i.lower() == "q":
        print("Exiting…")
        global_vars.KILL_THREADS = True
        time.sleep(0.5)
        exit()
