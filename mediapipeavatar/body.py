# MediaPipe Body
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision
from clientUDP import ClientUDP

import cv2
import threading
import time
import global_vars 
import struct

# 카메라 캡처 스레드
class CaptureThread(threading.Thread):
    cap = None
    ret = None
    frame = None
    isRunning = False
    counter = 0
    timer = 0.0
    def run(self):
        self.cap = cv2.VideoCapture(global_vars.CAM_INDEX)
        if global_vars.USE_CUSTOM_CAM_SETTINGS:
            self.cap.set(cv2.CAP_PROP_FPS, global_vars.FPS)
            self.cap.set(cv2.CAP_PROP_FRAME_WIDTH,global_vars.WIDTH)
            self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT,global_vars.HEIGHT)

        time.sleep(1)
        
        print("Opened Capture @ %s fps"%str(self.cap.get(cv2.CAP_PROP_FPS)))
        while not global_vars.KILL_THREADS:
            self.ret, self.frame = self.cap.read()
            self.isRunning = True
            if global_vars.DEBUG:
                self.counter = self.counter+1
                if time.time()-self.timer>=3:
                    print("Capture FPS: ",self.counter/(time.time()-self.timer))
                    self.counter = 0
                    self.timer = time.time()

# Mediapipe 포즈 처리 + Unity와 통신
class BodyThread(threading.Thread):
    data = ""
    dirty = True
    pipe = None
    timeSinceCheckedConnection = 0
    timeSincePostStatistics = 0

    def __init__(self):
        super().__init__()
        # 제스처 안정화/쿨다운(겹침 방지)
        self.min_hold = 0.25   # 같은 포즈가 이 시간 이상 유지되어야 발화
        self.cooldown = 0.8    # 직전 발화 이후 최소 대기 시간
        # 상태
        self.prev_gesture = "NONE"
        self.stable_since = 0.0
        self.last_fired_at = 0.0

    def run(self):
        mp_drawing = mp.solutions.drawing_utils
        mp_pose = mp.solutions.pose

        self.setup_comms()
        
        capture = CaptureThread()
        capture.start()

        with mp_pose.Pose(min_detection_confidence=0.80, 
                          min_tracking_confidence=0.5, 
                          model_complexity = global_vars.MODEL_COMPLEXITY,
                          static_image_mode = False,
                          enable_segmentation = True) as pose: 
            
            while not global_vars.KILL_THREADS and capture.isRunning==False:
                print("Waiting for camera and capture thread.")
                time.sleep(0.5)
            print("Beginning capture")
                
            while not global_vars.KILL_THREADS and capture.cap.isOpened():
                ti = time.time()

                # 캡처 스레드에서 프레임 가져오기
                ret = capture.ret
                image = capture.frame
                                
                image = cv2.flip(image, 1)
                image.flags.writeable = global_vars.DEBUG
                
                # Mediapipe 처리
                results = pose.process(image)
                tf = time.time()
                
                # 디버그용 시각화
                if global_vars.DEBUG:
                    if time.time()-self.timeSincePostStatistics>=1:
                        print("Theoretical Maximum FPS: %f"%(1/(tf-ti)))
                        self.timeSincePostStatistics = time.time()
                        
                    if results.pose_landmarks:
                        mp_drawing.draw_landmarks(
                            image, results.pose_landmarks, mp_pose.POSE_CONNECTIONS, 
                            mp_drawing.DrawingSpec(color=(255, 100, 0), thickness=2, circle_radius=4),
                            mp_drawing.DrawingSpec(color=(255, 255, 255), thickness=2, circle_radius=2),
                        )
                    cv2.imshow('Body Tracking', image)
                    cv2.waitKey(3)

                # Unity로 보낼 데이터 구성
                self.data = ""
                if results.pose_world_landmarks:
                    hand_world_landmarks = results.pose_world_landmarks.landmark
                    for i in range(0,33):
                        self.data += "{}|{}|{}|{}\n".format(
                            i, hand_world_landmarks[i].x, hand_world_landmarks[i].y, hand_world_landmarks[i].z
                        )

                    # 포즈 감지 → Unity 메시지 전송 (안정화/쿨다운 적용)
                    try:
                        self.detect_pose(hand_world_landmarks)
                    except Exception as e:
                        print("detect_pose() 호출 중 오류:", e)

                self.send_data(self.data)
                    
        self.pipe.close()
        capture.cap.release()
        cv2.destroyAllWindows()
        pass

    def setup_comms(self):
        if not global_vars.USE_LEGACY_PIPES:
            self.client = ClientUDP(global_vars.HOST,global_vars.PORT)
            self.client.start()
        else:
            print("Using Pipes for interprocess communication (not supported on OSX or Linux).")
        pass      

    def send_data(self,message):
        if not global_vars.USE_LEGACY_PIPES:
            self.client.sendMessage(message)
            pass
        else:
            if self.pipe==None and time.time()-self.timeSinceCheckedConnection>=1:
                try:
                    self.pipe = open(r'\\.\pipe\UnityMediaPipeBody1', 'r+b', 0)
                except FileNotFoundError:
                    print("Waiting for Unity project to run...")
                    self.pipe = None
                self.timeSinceCheckedConnection = time.time()

            if self.pipe != None:
                try:     
                    s = self.data.encode('utf-8') 
                    self.pipe.write(struct.pack('I', len(s)) + s)   
                    self.pipe.seek(0)    
                except Exception as ex:  
                    print("Failed to write to pipe. Is the unity project open?")
                    self.pipe= None
        pass

    # ===============================
    #           제스처 분류
    # ===============================
    # 반환: "BIGBALL" | "SMALLBALLS" | "MOON" | "SHIELD" | "NONE"
    def classify_gesture(self, lm):
        lw = lm[16]; rw = lm[15]
        ls = lm[12]; rs = lm[11]
        nose = lm[0]

        # 튜닝 가능한 임계값(상황에 따라 ±0.03~0.05 조정)
        BIGBALL_X_GAP   = 0.18  # 두 손목이 이 값보다 가까우면 큰 공(O)
        SMALLBALL_X_GAP = 0.28  # 이 값보다 멀면 작은 공(넓게)
        SHOULDER_Y_MARG = 0.05  # 어깨보다 최소 이만큼 위
        SIDE_GAP        = 0.20  # 달 공격: 좌우 벌림 정도
        Y_TOL           = 0.20  # 달 공격: 손목 높이가 어깨와 비슷

        # 편의: 손목 사이 x거리
        dx = abs(rw.x - lw.x)
        if global_vars.DEBUG:
            print(f"dx={dx:.3f}, lw.y={lw.y:.3f}, rw.y={rw.y:.3f}, ls.y={ls.y:.3f}, rs.y={rs.y:.3f}, nose.y={nose.y:.3f}")

        # 큰 공: 머리 위에서 두 손을 가깝게 모음(O)
        bigball = (lw.y < nose.y - 0.05) and (rw.y < nose.y - 0.05) and (dx < BIGBALL_X_GAP)

        # 작은 공: 두 손을 올렸지만 좌우로 넓게 벌림
        smallballs = (lw.y < ls.y - SHOULDER_Y_MARG) and (rw.y < rs.y - SHOULDER_Y_MARG) and (dx >= SMALLBALL_X_GAP)

        # 달 공격: 양팔 좌우로 뻗기(어깨 높이 부근)
        arms_side = (lw.x < ls.x - SIDE_GAP) and (rw.x > rs.x + SIDE_GAP) and \
                    (abs(lw.y - ls.y) < Y_TOL) and (abs(rw.y - rs.y) < Y_TOL)

        # 방패(X)
        x_pose = (abs(lw.x - rs.x) < 0.15) and (abs(rw.x - ls.x) < 0.15)

        # 우선순위(겹침 방지)
        if bigball:    return "BIGBALL"
        if smallballs: return "SMALLBALLS"
        if arms_side:  return "MOON"
        if x_pose:     return "SHIELD"
        return "NONE"

    # 안정화 + 쿨다운 + 단일 발화
    def detect_pose(self, landmarks):
        now = time.time()
        g = self.classify_gesture(landmarks)

        if g == self.prev_gesture:
            if self.stable_since == 0.0:
                self.stable_since = now
        else:
            self.prev_gesture = g
            self.stable_since = now if g != "NONE" else 0.0

        if g != "NONE" and self.stable_since > 0.0:
            if (now - self.stable_since) >= self.min_hold and (now - self.last_fired_at) >= self.cooldown:
                if g == "BIGBALL":
                    print("Detected Hands Up(O) → 거대한 공")
                    self.client.sendMessage("CREATE_BIGBALL")
                elif g == "SMALLBALLS":
                    print("Detected Hands Up Wide → 작은 공 3개")
                    self.client.sendMessage("CREATE_SPHERES")
                elif g == "MOON":
                    print("Detected Arms Side → 달 공격")
                    self.client.sendMessage("CREATE_MOON")
                elif g == "SHIELD":
                    print("Detected X Pose → 방패")
                    self.client.sendMessage("CREATE_SHIELD")
                self.last_fired_at = now
