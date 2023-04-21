const cardBtn = document.getElementById('billing-details');
const billingForm = document.querySelector('.billing-form');
const closeBtn = document.querySelector('.close-btn');
const paypalBtn = document.querySelector('.paypal-btn');

console.log(cardBtn)
console.log(billingForm)

cardBtn.addEventListener('click', () => {
    billingForm.classList.remove('d-none');
    paypalBtn.classList.remove('d-flex')
    paypalBtn.classList.add('d-none')
});

closeBtn.addEventListener('click', () => {
    billingForm.classList.add('d-none');
    paypalBtn.classList.add('d-flex')
    paypalBtn.classList.remove('d-none')
});
