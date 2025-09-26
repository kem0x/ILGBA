export DEVKITPRO=/opt/devkitpro
export DEVKITARM=$DEVKITPRO/devkitARM
export PATH=$DEVKITARM/bin:$PATH

arm-none-eabi-gcc -c minimal_blue.c -mthumb-interwork -mthumb -O2 -o minimal_blue.o
arm-none-eabi-gcc minimal_blue.o -mthumb-interwork -mthumb -specs=gba.specs -o minimal_blue.elf
arm-none-eabi-objcopy -v -O binary minimal_blue.elf minimal_blue.gba
gbafix minimal_blue.gba