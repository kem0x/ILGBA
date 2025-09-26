export DEVKITPRO=/opt/devkitpro
export DEVKITARM=$DEVKITPRO/devkitARM
export PATH=$DEVKITARM/bin:$PATH

arm-none-eabi-gcc -c infinite.c -mthumb-interwork -mthumb -O2 -o infinite.o
arm-none-eabi-gcc infinite.o -mthumb-interwork -mthumb -specs=gba.specs -o infinite.elf
arm-none-eabi-objcopy -v -O binary infinite.elf infinite.gba
gbafix infinite.gba