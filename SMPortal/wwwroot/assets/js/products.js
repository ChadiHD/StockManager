const productList = document.getElementById('products-list')

console.log(productList)

const jackets = document.getElementById('jackets')
const hoodies = document.getElementById('hoodies')
const shirts = document.getElementById('shirts')
const jeans = document.getElementById('jeans')
const cargo = document.getElementById('cargo')

jackets.addEventListener('click', function() {
    productList.innerHTML = `
    <div class="col-lg-4 col-md-4 all des">
                <div class="product-item">
                  <a href="#"><img class="decrease-height" src="../assets/images/products/jackets/1.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Green Baseball jacket</h4>
                    </a>
                    <h6>$47.25</h6>
                    <p class="jackets">Green Baseball jacket- soft felted fabric with lightly padded sleeves in a contrasting colour. Regular fit with press-studs down the front and welt front pockets with a press-stud. Small, ribbed stand-up collar and matching ribbing at the cuffs and hem.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (12)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all dev">
                <div class="product-item align-bottom">
                  <a href="#"><img class="decrease-height" src="../assets/images/products/jackets/2.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4 class="decrease-font-m">Black Lightweight puffer jacket</h4>
                    </a>
                    <h6>$55.50</h6>
                    <p class="jackets">Black Lightweight puffer jacket- woven fabric with a hood and discreet zip down the front with a chin guard. Pockets in the side seams with a concealed zip, and narrow elastication at the cuffs and hem. Unlined.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (24)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all gra">
                <div class="product-item">
                  <a href="#"><img class="decrease-height" src="../assets/images/products/jackets/3.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4 class="decrease-font">Brown Lightly padded jacket</h4>
                    </a>
                    <h6>$42.75</h6>
                    <p class="jackets">Brown Lightly padded jacket- woven fabric with embroidered motifs front and back. Regular fit with a collar, zip down the front, and long sleeves. Diagonal welt front pockets with a press-stud and an inner pocket with a press-stud. Quilted lining.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (36)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all gra">
                <div class="product-item">
                  <a href="#"><img class="decrease-height" src="../assets/images/products/jackets/4.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Light Grey Regular</h4>
                    </a>
                    <h6>$50.25</h6>
                    <p class="jackets">Light Grey Regular- fit jacket- windbreaker in windproof. Lined hood with an elastic drawstring, a zip down the front and long sleeves. Diagonal side pockets with a zip and an inner pocket with a press-stud. Covered elastication and an adjustable hook.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (48)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all dev">
                <div class="product-item">
                  <a href="#"><img class="decrease-height" src="../assets/images/products/jackets/5.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Multi Green jacket</h4>
                    </a>
                    <h6>$44.25</h6>
                    <p class="jackets">Multi Green jacket- patterned cotton twill with a collar, metal buttons down the front, open chest pockets and a yoke at the back. Relaxed fit with dropped shoulders, long sleeves with buttoned cuffs, and a straight-cut hem. Unlined.
                    </p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (60)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all des">
                <div class="product-item">
                  <a href="#"><img class="decrease-height" src="../assets/images/products/jackets/6.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>White Track jacket</h4>
                    </a>
                    <h6>$59.00</h6>
                    <p class="jackets">White Track jacket- jacquard-weave terry with an embroidered motif on the chest and narrow piping in a contrasting colour over the chest and down the sleeves. Relaxed fit with a stand-up collar, zip down the front, long sleeves and side pockets.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (72)</span>
                  </div>
                </div>
              </div>
    `;
});

hoodies.addEventListener('click', function() {
    productList.innerHTML = `
        <div class="col-lg-4 col-md-4 all des">
        <div class="product-item">
        <a href="#"><img class="decrease-height" src="../assets/images/products/hoodies/1.webp" alt=""></a>
        <div class="down-content">
            <a href="#">
            <h4>Black Oversized hoodie</h4>
            </a>
            <h6>$23.50</h6>
            <p class="hoodies">Black Oversized hoodie-cotton sweatshirt fabric with a soft brushed inside and a motif on the front. Lined wrapover hood, dropped shoulders, long sleeves, a kangaroo pocket and ribbing at the cuffs and hem.</p>
            <ul class="stars">
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (12)</span>
        </div>
        </div>
    </div>
    <div class="col-lg-4 col-md-4 all dev">
        <div class="product-item">
        <a href="#"><img class="decrease-height" src="../assets/images/products/hoodies/2.webp" alt=""></a>
        <div class="down-content">
            <a href="#">
            <h4>Cream Hoodie</h4>
            </a>
            <h6>$31.75</h6>
            <p class="hoodies">Cream Hoodie- sweatshirt fabric made from a cotton blend with a soft brushed inside. Relaxed fit with a lined hood, kangaroo pocket, dropped shoulders, long sleeves and ribbing at the cuffs and hem.</p>
            <ul class="stars">
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (24)</span>
        </div>
        </div>
    </div>
    <div class="col-lg-4 col-md-4 all gra">
        <div class="product-item">
        <a href="#"><img class="decrease-height" src="../assets/images/products/hoodies/3.webp" alt=""></a>
        <div class="down-content">
            <a href="#">
            <h4 class="decrease-font">Dark Grey Oversized hoodie</h4>
            </a>
            <h6>$24.25</h6>
            <p class="hoodies">Dark Grey Oversized hoodie- sweatshirt fabric made from a cotton blend with a soft brushed inside. Double-layered wrapover hood, low dropped shoulders, long sleeves, a kangaroo pocket and ribbing at the cuffs and hem.</p>
            <ul class="stars">
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (36)</span>
        </div>
        </div>
    </div>
    <div class="col-lg-4 col-md-4 all gra">
        <div class="product-item">
        <a href="#"><img class="decrease-height" src="../assets/images/products/hoodies/4.webp" alt=""></a>
        <div class="down-content">
            <a href="#">
            <h4>Light beige Hoodie</h4>
            </a>
            <h6>$28.25</h6>
            <p class="hoodies">Light beige Hoodie- sweatshirt fabric made from a cotton blend with a print motif. Relaxed fit with a double-layered, drawstring hood, long sleeves, kangaroo pocket and ribbing at the cuffs and hem. Soft brushed inside.</p>
            <ul class="stars">
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (48)</span>
        </div>
        </div>
    </div>
    <div class="col-lg-4 col-md-4 all dev">
        <div class="product-item">
        <a href="#"><img class="decrease-height" src="../assets/images/products/hoodies/5.webp" alt=""></a>
        <div class="down-content">
            <a href="#">
            <h4>Light purple Hoodie</h4>
            </a>
            <h6>$34.00</h6>
            <p class="hoodies">Light purple Hoodie- sweatshirt fabric made from a cotton blend. Relaxed fit with a jersey-lined, drawstring hood, a kangaroo pocket, long sleeves and wide ribbing at the cuffs and hem. Soft brushed inside.
            </p>
            <ul class="stars">
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (60)</span>
        </div>
        </div>
    </div>
    <div class="col-lg-4 col-md-4 all des">
        <div class="product-item">
        <a href="#"><img class="decrease-height" src="../assets/images/products/hoodies/6.webp" alt=""></a>
        <div class="down-content">
            <a href="#">
            <h4>Purple Hoodie</h4>
            </a>
            <h6>$38.25</h6>
            <p class="hoodies">Purple Hoodie- sweatshirt fabric made from a cotton blend. Relaxed fit with a lined hood, kangaroo pocket, dropped shoulders, long sleeves and ribbing at the cuffs and hem. Soft brushed inside.</p>
            <ul class="stars">
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (72)</span>
        </div>
        </div>
    </div>
    `;
});

shirts.addEventListener('click', function() {
    productList.innerHTML = `
        <div class="col-lg-4 col-md-4 all des">
        <div class="product-item">
            <a href="#"><img class="decrease-height" src="../assets/images/products/t_shirts/1.webp" alt=""></a>
            <div class="down-content">
            <a href="#">
                <h4>Black Relaxed-fit T-shirt</h4>
            </a>
            <h6>$11.25</h6>
            <p class="t-shirts">Black Relaxed-fit T-shirt- soft printed cotton jersey with a rib-trimmed neckline, dropped shoulders and a straight-cut hem.</p>
            <ul class="stars">
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (12)</span>
            </div>
        </div>
        </div>
        <div class="col-lg-4 col-md-4 all dev">
        <div class="product-item">
            <a href="#"><img class="decrease-height" src="../assets/images/products/t_shirts/2.webp" alt=""></a>
            <div class="down-content">
            <a href="#">
                <h4 class="decrease-font">Brown Regular-fit polo shirt</h4>
            </a>
            <h6>$14.00</h6>
            <p class="t-shirts">Brown Regular-fit polo shirt- soft fine-knit cotton blend with a collar, small V-shaped opening at the top and short sleeves. Fine ribbing at the cuffs and hem.</p>
            <ul class="stars">
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (24)</span>
            </div>
        </div>
        </div>
        <div class="col-lg-4 col-md-4 all gra">
        <div class="product-item">
            <a href="#"><img class="decrease-height" src="../assets/images/products/t_shirts/3.webp" alt=""></a>
            <div class="down-content">
            <a href="#">
                <h4>Creme Relaxed-fit T-shirt</h4>
            </a>
            <h6>$12.75</h6>
            <p class="t-shirts">Creme Relaxed-fit T-shirt- soft printed cotton jersey with a rib-trimmed neckline, dropped shoulders and a straight-cut hem.</p>
            <ul class="stars">
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (36)</span>
            </div>
        </div>
        </div>
        <div class="col-lg-4 col-md-4 all gra">
        <div class="product-item">
            <a href="#"><img class="decrease-height" src="../assets/images/products/t_shirts/4.webp" alt=""></a>
            <div class="down-content">
            <a href="#">
                <h4 class="decrease-font-m">Light Brown Relaxed-fit T-shirt</h4>
            </a>
            <h6>$17.25</h6>
            <p class="t-shirts">Light Brown Relaxed-fit T-shirt- soft cotton jersey with a print motif, round, rib-trimmed neckline, dropped shoulders and a straight-cut hem.</p>
            <ul class="stars">
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (48)</span>
            </div>
        </div>
        </div>
        <div class="col-lg-4 col-md-4 all dev">
        <div class="product-item">
            <a href="#"><img class="decrease-height" src="../assets/images/products/t_shirts/5.webp" alt=""></a>
            <div class="down-content">
            <a href="#">
                <h4 class="decrease-font">Navy Blue Relaxed-fit T-shirt</h4>
            </a>
            <h6>$20.50</h6>
            <p class="t-shirts">Navy Blue Relaxed-fit T-shirt- soft cotton jersey with a finely ribbed trim along the round neckline. Dropped shoulders and a straight-cut hem.
            </p>
            <ul class="stars">
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (60)</span>
            </div>
        </div>
        </div>
        <div class="col-lg-4 col-md-4 all des">
        <div class="product-item">
            <a href="#"><img class="decrease-height" src="../assets/images/products/t_shirts/6.webp" alt=""></a>
            <div class="down-content">
            <a href="#">
                <h4>White Oversized T-shirt</h4>
            </a>
            <h6>$23.75</h6>
            <p class="t-shirts">White Oversized T-shirt- soft cotton jersey with a print motif. Rib-trimmed neckline, dropped shoulders and a straight-cut hem.</p>
            <ul class="stars">
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
                <li><i class="fa fa-star"></i></li>
            </ul>
            <span>Reviews (72)</span>
            </div>
        </div>
        </div>
    `;
});

jeans.addEventListener('click', function() {
    productList.innerHTML = `
    <div class="col-lg-4 col-md-4 all des">
    <div class="product-item">
      <a href="#"><img src="../assets/images/products/jeans/1.webp" alt=""></a>
      <div class="down-content">
        <a href="#">
          <h4>Black Jeans</h4>
        </a>
        <h6>$26.50</h6>
        <p class="jeans">Black Jeans- 5-pocket jeans in cotton denim with a slight stretch for good comfort. Straight leg and a slim fit from the waist through the thigh to the hem. Regular waist and a zip fly. Easily styled for sleek or sporty.</p>
        <ul class="stars">
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
        </ul>
        <span>Reviews (12)</span>
      </div>
    </div>
  </div>
  <div class="col-lg-4 col-md-4 all dev">
    <div class="product-item">
      <a href="#"><img src="../assets/images/products/jeans/2.webp" alt=""></a>
      <div class="down-content">
        <a href="#">
          <h4>Dark Grey Jeans</h4>
        </a>
        <h6>$39.75</h6>
        <p class="jeans">Dark Grey Jeans- 5-pocket jeans in cotton denim with a premium stretch for excellent comfort. Skinny fit from the waist to the hem, a regular waist and a zip fly. Designed for everyday wear.</p>
        <ul class="stars">
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
        </ul>
        <span>Reviews (24)</span>
      </div>
    </div>
  </div>
  <div class="col-lg-4 col-md-4 all gra">
    <div class="product-item">
      <a href="#"><img src="../assets/images/products/jeans/3.webp" alt=""></a>
      <div class="down-content">
        <a href="#">
          <h4>Denim Blue Jeans</h4>
        </a>
        <h6>$31.25</h6>
        <p class="jeans">Denim Blue Jeans- 5-pocket jeans in rigid cotton denim with a straight leg and a relaxed fit from the seat to the hem with extra room from the thigh down. Regular waist and a zip fly. Designed for everyday wear.</p>
        <ul class="stars">
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
        </ul>
        <span>Reviews (36)</span>
      </div>
    </div>
  </div>
  <div class="col-lg-4 col-md-4 all gra">
    <div class="product-item">
      <a href="#"><img src="../assets/images/products/jeans/4.webp" alt=""></a>
      <div class="down-content">
        <a href="#">
          <h4>Denim Blue Jeans</h4>
        </a>
        <h6>$37.00</h6>
        <p class="jeans">Denim Blue Jeans- 5-pocket jeans in stretch cotton denim with attention paid to low-impact washes using less water and energy. Regular fit with relaxed legs – the classic fit, defined. Zip fly and button.</p>
        <ul class="stars">
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
        </ul>
        <span>Reviews (48)</span>
      </div>
    </div>
  </div>
  <div class="col-lg-4 col-md-4 all dev">
    <div class="product-item">
      <a href="#"><img src="../assets/images/products/jeans/5.webp" alt=""></a>
      <div class="down-content">
        <a href="#">
          <h4>Light Denim Jeans</h4>
        </a>
        <h6>$27.25</h6>
        <p class="jeans">Light Denim Jeans- 5-pocket jeans in denim with a slight stretch for good comfort. Straight leg and a slim fit from the waist through the thigh to the hem. Regular waist and a zip fly. Made using Lycra® Freefit® technology for soft, super-generous stretch.
        </p>
        <ul class="stars">
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
        </ul>
        <span>Reviews (60)</span>
      </div>
    </div>
  </div>
  <div class="col-lg-4 col-md-4 all des">
    <div class="product-item">
      <a href="#"><img src="../assets/images/products/jeans/6.webp" alt=""></a>
      <div class="down-content">
        <a href="#">
          <h4>White Jeans</h4>
        </a>
        <h6>$43.25</h6>
        <p class="jeans">White Jeans- 5-pocket jeans in stretch cotton denim with attention paid to low-impact washes using less water and energy. Regular fit with relaxed legs – the classic fit, defined. Zip fly and button.</p>
        <ul class="stars">
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
          <li><i class="fa fa-star"></i></li>
        </ul>
        <span>Reviews (72)</span>
      </div>
    </div>
  </div>
    `;
});

cargo.addEventListener('click', function() {
    productList.innerHTML = `
              <div class="col-lg-4 col-md-4 all des">
                <div class="product-item">
                  <a href="#"><img src="../assets/images/products/cargo/1.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Beige Trousers</h4>
                    </a>
                    <h6>$22.75</h6>
                    <p class="cargo">Beige Trousers in cotton twill with covered elastication and a concealed drawstring at the waist.
                      Regular fit with a zip fly and button, side pockets, welt back pockets and flap leg pockets with
                      concealed press-studs.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (12)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all dev">
                <div class="product-item">
                  <a href="#"><img src="../assets/images/products/cargo/2.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Grey Trousers</h4>
                    </a>
                    <h6>$27.25</h6>
                    <p class="cargo">Grey Trousers in cotton twill with covered elastication and a concealed drawstring at the waist. Regular fit with a zip fly and button, side pockets, welt back pockets and flap leg pockets with concealed press-studs.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (24)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all gra">
                <div class="product-item">
                  <a href="#"><img src="../assets/images/products/cargo/3.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Khaki Green Joggers</h4>
                    </a>
                    <h6>$34.50</h6>
                    <p class="cargo">Joggers in a cotton weave with covered elastication and a drawstring at the waist, diagonal side pockets and flap back and leg pockets with concealed press-studs. Tapered legs with seams and darts at the knees and covered elastication at the hems.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (36)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all gra">
                <div class="product-item">
                  <a href="#"><img src="../assets/images/products/cargo/4.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Black Joggers</h4>
                    </a>
                    <h6>$30.75</h6>
                    <p class="cargo">Joggers in a cotton weave with covered elastication and a drawstring at the waist, diagonal side pockets and flap back and leg pockets with concealed press-studs. Tapered legs with seams and darts at the knees and covered elastication at the hems.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (48)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all dev">
                <div class="product-item">
                  <a href="#"><img src="../assets/images/products/cargo/5.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Blue Joggers</h4>
                    </a>
                    <h6>$24.00</h6>
                    <p class="cargo">Joggers in a cotton weave with covered elastication and a drawstring at the waist, diagonal side pockets and flap back and leg pockets with concealed press-studs. Tapered legs with seams and darts at the knees and covered elastication at the hems.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (60)</span>
                  </div>
                </div>
              </div>
              <div class="col-lg-4 col-md-4 all des">
                <div class="product-item">
                  <a href="#"><img src="../assets/images/products/cargo/6.webp" alt=""></a>
                  <div class="down-content">
                    <a href="#">
                      <h4>Green Trousers</h4>
                    </a>
                    <h6>$38.25</h6>
                    <p class="cargo">Trousers in a hard-wearing ripstop weave. Covered elastication and a drawstring at the waist, and a zip fly and button. Side, back and leg pockets with a flap and concealed press-studs, and an elastic drawstring with a cord stopper.</p>
                    <ul class="stars">
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                      <li><i class="fa fa-star"></i></li>
                    </ul>
                    <span>Reviews (72)</span>
                  </div>
                </div>
              </div>
    `;
});