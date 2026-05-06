Project Objective

\- This is the Unity 3DProject

\- Development of a digital twin-based earthquake simulation for a graduation project

\- Therefore, maintenance is not required after completion

\- The development platform consists of a Hololens2 platform to be used as the client and a Unity program on an Ubuntu-based server to be used as a headless system

The project is using MRTK3. The packages in use are as follows:

Microsoft Spatializer: 2.0.55

Mixed Reality OpenXR Plugin: 1.11.2

Mixed Reality Scene Understanding: 0.6.0

Mixed Reality Toolkit GPU Stats: 1.0.3

Mixed Reality Toolkit Microphone Stream Selector: 1.0.0

MRTK Graphics Tools: 0.7.1

MRTK Windows Text-to-Speech Plugin: 1.0.4

MRTK Accessibility Early Preview: 1.0.3-pre.20

MRTK Audio Effects: 3.0.4

MRTK Core Definitions: 4.0.0-pre.1

MRTK Diagnostics: 3.0.2

MRTK Extended Assets: 3.0.3

MRTK Input: 4.0.0-pre.1

MRTK Spatial Manipulation: 4.0.0-pre.1

MRTK Standard Assets: 3.2.0

MRTK Tools: 3.0.4

MRTK UX Components: 4.0.0-pre.1

MRTK UX Components (Non-Canvas): 4.0.0-pre.1

MRTK UX Core Scripts: 4.0.0-pre.1

MRTK Windows Speech: 3.0.3



Project Features

\- Users log in with their accounts

\- After logging in, two buttons appear (Create Room / Start Simulation)

\- When creating a room, the room (walls/floor/ceiling) is recognized via Hololens2

\- Subsequently, furniture is recognized and placed in locations identical to the actual space

\- To achieve this, details of the furniture (weight, presence of items inside, fine-tuning, furniture adjustment status, etc.) must be manually adjusted

\- Simulation can only proceed if an actual room has been created

\- When "Simulate" is pressed, the risk of the rooms created so far is displayed

\- When a room is clicked, the simulation results for that room are displayed if any

\- After clicking a room, the UI allows for editing, starting the simulation, or deleting the room

\- When the simulation is running, it displays the occurrence of an actual earthquake. However, the physical calculations simulating the actual earthquake are executed on the server. The client, hololens, receives simple simulation results in real-time or all at once and only outputs them.



\- Outputs the resulting results (indicating which furniture is dangerous, overall furniture rearrangement plans, etc.)



Notes

\- The above guidelines have been translated from Korean into English.



Precautions for Writing Code

\- When reflecting modifications, be sure to verify and confirm what and how to change them before proceeding.

\- Project descriptions should be in Korean.

\- Comments may be written in Korean, but should be kept as short and concise as possible.

\- Do not use emojis in comments.

\- The script is located in the following folder: Assets/Scripts

\- Fixed to use a specific font from the inspector when entering text.

