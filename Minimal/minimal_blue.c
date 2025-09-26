// Minimal blue screen in Mode 3
#define REG_DISPCNT (*(volatile unsigned short*)0x4000000)
#define MODE3 0x0003
#define BG2   0x0400
#define VRAM  ((volatile unsigned short*)0x6000000)

int main(void) {
    REG_DISPCNT = MODE3 | BG2;       // mode 3, BG2 on
    for (int y = 0; y < 160; y++) {
        for (int x = 0; x < 240; x++) {
            VRAM[y*240 + x] = 0x7C00;  // blue (BGR555)
        }
    }
    for(;;) { /* hang */ }
}
