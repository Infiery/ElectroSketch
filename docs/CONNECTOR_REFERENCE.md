# Connector reference

The in-app pin names are based on the USB-IF USB 2.0, USB 3.x legacy connector, and USB Type-C cable and connector specifications.

The library models these physical connector families:

- USB 2.0 Standard-A and Standard-B
- USB 2.0 Mini-A, Mini-B, and Mini-AB
- USB 2.0 Micro-A, Micro-B, and Micro-AB
- USB SuperSpeed Standard-A and Standard-B
- USB SuperSpeed Micro-A, Micro-B, and Micro-AB
- USB Type-C cable plug and receptacle
- Lightning cable plug and device receptacle

Connector shells are exposed as `SH`. USB-C receptacles expose A1–A12 and B1–B12. USB-C cable plugs expose their defined plug contacts; A5 is the modeled CC contact and plug B5 is VCONN. Receptacle A5 and B5 remain distinct CC1/CC2 contacts. Every contact is independently addressable so breakout-board and partial-wiring examples can be represented.

The Lightning parts expose contacts L1–L8 and the shell plus separate abstract VBUS/GND terminals for basic power topology. Ready Lightning cables show only the simplified VBUS and GND paths; those model terminals are not assigned to physical Lightning contact numbers or accessory-control behavior.

Ready cable guide: the guide stays closed until requested and reports the last selected part, its model paths, and current external attachments. A full-featured passive USB-C-to-C cable models 16 routes: VBUS, GND, CC, USB 2.0 D+/D−, four differential SuperSpeed pairs, SBU1/SBU2, and shield. TX pairs route to RX pairs in each direction. A default plug orientation is drawn; flipping a plug changes which duplicate receptacle contact is used. The cable is modeled as a passive 3 A cable. USB Power Delivery negotiation is not simulated, so even the 65 W charger object remains at its default 5 V output.

Lightning ready cables model VBUS and GND for basic power topology practice. USB-A-to-USB-B SuperSpeed cables include the USB 2.0 conductors plus TX/RX lane crossover. These diagrams focus on basic electrical paths, not protocol negotiation.

USB 2.0's conventional cable colors are red VBUS, black GND, green D+ and white D−. Extra colors inside USB-C cables are not standardized across manufacturers. Harmonized European flexible-cord identification is brown live, blue neutral and green/yellow protective earth; identify a real conductor by markings and tests, never its color alone.

Primary references:

- https://www.usb.org/document-library/usb-type-cr-cable-and-connector-specification-release-25
- https://www.usb.org/usb-charger-pd
- https://www.usb.org/documents?search=USB%202.0%20Specification
- https://www.usb.org/documents?search=USB%203.1%20Legacy%20Cable%20and%20Connector
