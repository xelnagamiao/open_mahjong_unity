
SAMPLE_IMAGES = \
	tile-00-0.4.png tile-01-0.4.png tile-02-0.4.png \
	tile-03-0.4.png tile-04-0.4.png tile-05-0.4.png \
	tile-06-0.4.png tile-07-0.4.png tile-08-0.4.png \
	tile-09-0.4.png tile-10-0.4.png tile-11-0.4.png \
	tile-12-0.4.png tile-13-0.4.png tile-14-0.4.png \
	tile-15-0.4.png tile-16-0.4.png tile-17-0.4.png \
	tile-18-0.4.png tile-19-0.4.png tile-20-0.4.png \
	tile-21-0.4.png tile-22-0.4.png tile-23-0.4.png \
	tile-24-0.4.png tile-25-0.4.png tile-26-0.4.png \
	tile-27-0.4.png tile-28-0.4.png tile-29-0.4.png \
	tile-30-0.4.png tile-31-0.4.png tile-32-0.4.png \
	tile-33-0.4.png tile-34-0.4.png tile-35-0.4.png \
	tile-36-0.4.png tile-37-0.4.png tile-38-0.4.png \
	tile-39-0.4.png tile-40-0.4.png tile-41-0.4.png \
	tile-42-0.4.png


all: samples

smooth.png: build-smooth.pl face-images/*.png
	./build-smooth.pl

samples: smooth.png
	mkdir -p samples
	( cd tmp/; \
	for i in $(SAMPLE_IMAGES); \
	do \
		convert $$i -strip ../samples/$$i; \
	done; )

clean:
	-rm -rf smooth.png tmp *~
