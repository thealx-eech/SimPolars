# SimPolars
MSFS polars capture application, slightly modified MSFS SimVars example app.

To compile it you need:
- MS Visual Studio 2019
- MSFS SDK installed


How to use:

1. Launch MSFS
2. Launch application, request with all necessary flight model variables will be loaded as #0
3. Press "CONNECT" button
4. select Variable frequesncy option if necessary
5. Open tab "Polars"
6. Load polars image if you have aircraft aerodynamics documentation
7. Adjust min/max values, units are not available yet
8. In the game:
8.1 activate autopilot trim if it available (Z key)
8.2 reach maximum speed
8.3 level aircraft
8.4 set required total weight
8.5 set flaps position
9. Press "ENABLE DATA CAPTURE" button
10. Wait until aircraft will reach minimum speed, if necessary - disable auto trim and raise nose slowly to decrease the speed
11. Press "DISABLE DATA CAPTURE"
12. Change flaps position and repeat the test. Multiple weight tests for same flap position not supported yet.
13. Captured data can be saved as JSON file
14. to clear data of current flap, press CLEAR button

In version 0.3 request scripts was altered to support structured data responce (borrowed from FsConnect project). This method provide
best timing precision, as flight model data and absolute time value are taken from the same frame.
List of request #0 variables hardcoded in:
 
 * struct PlaneInfoResponse
 * List<SimvarRequest> getFlightDataVariables
 
So to add extra vars to this package, you need to add variables manually, respecting order.
This package is read only, so if you need to adjust some of it - add it as single simvar manually.

Additional information and latest updates available at https://msfs.touching.cloud


