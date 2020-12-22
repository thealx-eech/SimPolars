# SimPolars
MSFS polars capture application, slightly modified MSFS SimVars example app.

To compile it you need:
- MS Visual Studio 2019
- MSFS SDK installed


How to use:

1. Launch MSFS
2. Launch application, polars.simvars file with all required variables will be loaded
3. Press "CONNECT" button
4. Open tab "Polars"
5. Load polars image if you have aircraft aerodynamics documentation
6. Adjust min/max values, units are not available yet
7. In the game:
7.1 activate autopilot trim if it available (Z key)
7.2 reach maximum speed
7.3 level aircraft
7.4 set required total weight
7.5 set flaps position
8. Press "ENABLE DATA CAPTURE" button
9. Wait until aircraft will reach minimum speed, if necessary - disable auto trim and raise nose slowly to decrease the speed
10. Press "DISABLE DATA CAPTURE" or just reach minimum speed, capture process will be disabled automatically
11. Change flaps position and repeat the test. Multiple weight tests for same flap position not supported yet.
12. Captured data can be saved as JSON file

Additional information and latest updates available at https://msfs.touching.cloud


