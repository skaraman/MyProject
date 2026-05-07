Esperanza has forms -

each Form has its own ablities and equipment

when Esperanza changes forms her abilities and equipment change to that form

All forms have these default animations -
Walk
Run
Sprint
Stance
Breathe
Jump
JumpDouble
JumpLanding
JumpFalling
Dance
Block
Dodge
and all the defined xToY for these at Interrupts.cs where X and Y are one of the animations above (eg WalkToRun)

Forms have 9 unique moves which can vary depending on player choice but they will always only be 9

So when loading the character we only need to load -

The equipped items sheets that have the animations that are tied to the abilities and all the default animations
