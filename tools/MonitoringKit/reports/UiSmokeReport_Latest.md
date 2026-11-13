# Lumiere UI Automation Smoke Test Report
**Timestamp:** 2026-11-10 20:16:19  
**Overall Status:** FAILED  

### Summary
| Metric | Value |
|---|---|
| **Total Scenarios** | 10 |
| **Passed** | 9 |
| **Warnings** | 0 |
| **Failed** |  |

### Step Results
| Scenario | Status | Details | Time |
|---|---|---|---|
| Process Launch | PASS | Started LumiereMediaPlayer.exe (PID: 18140) | 20:16:12 |
| Window Hook | PASS | Found main UI Automation window 'Lumière Media Player' in 2233ms | 20:16:14 |
| Window Pattern Supported | PASS | VisualState: Normal, CanMaximize: True, CanMinimize: True, IsModal: False | 20:16:14 |
| Window Bounding Box | PASS | Valid dimensions: 1200 x 800 at (360, 110) | 20:16:14 |
| Visual Tree Population | PASS | Discovered 43 interactive UI Automation elements in tree | 20:16:15 |
| Fullscreen Transition Request | FAIL | Window bounds did not expand for fullscreen (1200x800 -> 1200x800) | 20:16:17 |
| Exit Fullscreen Transition | PASS | Dispatched F11 restore. Window restored to 1200 x 800 | 20:16:18 |
| Crash Log Audit | PASS | Zero unhandled crashes detected during session | 20:16:18 |
| Visual Diagnostics Telemetry | PASS | No swapchain occlusion anomalies recorded in playback log | 20:16:18 |
| Graceful Shutdown | PASS | Process exited cleanly (ExitCode: 0) | 20:16:18 |
